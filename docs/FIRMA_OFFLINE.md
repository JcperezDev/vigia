# Firma fuera del PC de desarrollo y relevo de clave A → B

Procedimiento para firmar Vigía en un **equipo de firma** separado, sin red, y para pasar la confianza de la clave A
(la de 0.20.17, posiblemente expuesta) a una clave nueva B. Lo ejecuta el dueño de las claves. Ningún agente, script del
PC de desarrollo ni servicio externo toca una clave privada real.

Pasos marcados **⏸ PARADA**: los hace el dueño en persona (frase, copia de una clave privada, firma). Todo lo demás son
comprobaciones sin secretos.

## Qué hay y qué falta (31‑10‑2026)

| | Estado |
|---|---|
| Clave A | `~/.vigia/release-key.pem` en el PC de desarrollo, **sin cifrar**. Permisos: SYSTEM, Administradores y tu cuenta (sin `CodexSandboxUsers`). Antes del 30‑10 la carpeta daba lectura a `CodexSandboxUsers`: **no se puede descartar que se leyera**. |
| Clave B | **No existe.** Se crea en el equipo de firma (paso 2). |
| Equipo de firma | **Sin preparar.** |
| Herramientas | `VigiaFirma.exe` (`tools/signing/estacion`, se compila en `out/vigiafirma/`) y `tools/signing/firmar-fuera.ps1`. Pruebas con claves de laboratorio: `estacion-test` 49/0, `signing-test` 48/0. |
| Huella de la pública A | `ab29dbf9ab37ae1848561d63b1811c0c764c2e37282e69aa82810b147136bc08` (`tools/signing/claves-publicas/vigia-A.publica.txt`; igual en Puente, en Vigía y en v0.20.17) |

## Riesgo de A que el relevo NO quita

Quien tenga una copia de A puede firmar una actualización falsa que aceptarán **todas** las instalaciones que confían en A:

- 0.20.x (Puente 1.4.0, solo A) — hasta que se actualicen;
- 0.21.0 de transición (Puente 2.0.2, A y B) — hasta que instalen la versión que retira A (Puente 2.0.3, solo B).

Una actualización falsa firmada con A puede instalar un Puente que confíe en la clave del atacante; esa instalación ya no
vuelve sola al camino bueno. La cadena A → B **reduce la ventana, no la cierra**. Para acortarla: publicar la versión que
retira A (paso 9) en cuanto la transición esté publicada y comprobada, sin esperar a otras novedades.

Ruta de recuperación (para quien tenga dudas o se haya quedado en 0.20.x): instalar a mano desde la página de la versión,
comprobando antes el instalador por un canal distinto de la propia descarga — ver «Recuperación» al final.

## Material

- Equipo de firma: Windows 10/11 x64 que no sea el de desarrollo. Cuenta **local** dedicada (p. ej. `firma`), sin cuenta
  Microsoft, sin OneDrive ni copias en la nube, sin agentes ni asistentes. BitLocker en el disco si se puede.
  **Sin red**: cable fuera y Wi‑Fi apagado (mejor desactivado en el administrador de dispositivos). `firmar-fuera.ps1`
  se niega a firmar si ve un adaptador activo.
- Cuatro memorias USB, rotuladas: **TRASLADO** (va y vuelve), **HOJA** (solo las huellas), **RESPALDO‑1** y **RESPALDO‑2**.
- Papel para anotar la frase y las huellas.
- En el equipo de firma no hace falta instalar .NET: `VigiaFirma.exe` y `Puente.exe` son autocontenidos; PowerShell 5.1
  viene con Windows.

## 1. Preparar el equipo de firma

1. En el PC de desarrollo, copia a **TRASLADO**: `out/vigiafirma/VigiaFirma.exe`, `tools/signing/firmar-fuera.ps1` y
   `tools/signing/claves-publicas/vigia-A.publica.txt`.
2. En el equipo de firma, con la red ya cortada, crea `C:\VigiaFirma\` y `C:\VigiaFirma\claves\`, copia los tres
   archivos a `C:\VigiaFirma\` y comprueba sus huellas con una herramienta de Windows (no con las nuestras):
   ```powershell
   Get-FileHash C:\VigiaFirma\VigiaFirma.exe, C:\VigiaFirma\firmar-fuera.ps1, C:\VigiaFirma\vigia-A.publica.txt
   ```
   Tienen que coincidir enteras con las del informe `SECURITY_KEY_REVIEW.md` («Herramientas del equipo de firma»).
   Si alguna no coincide, para.
3. Permisos de la carpeta de claves: solo tu cuenta, sin herencia.
   ```powershell
   icacls C:\VigiaFirma\claves /inheritance:r /grant:r "$env:USERNAME:(OI)(CI)F"
   ```

## 2. Crear B (en el equipo de firma) — ⏸ PARADA: frase

```powershell
cd C:\VigiaFirma
.\VigiaFirma.exe nueva C:\VigiaFirma\claves B
```
Pide la frase del respaldo dos veces (16 caracteres o más; no se ve al escribir). Escríbela también en papel.
Deja tres archivos:

| Archivo | Qué es | Dónde va |
|---|---|---|
| `vigia-B.dpapi` | clave de uso, cifrada con DPAPI de **esta cuenta en este equipo** (en otro equipo no abre) | se queda aquí |
| `vigia-B.respaldo.pem` | respaldo cifrado con la frase (PKCS#8, AES‑256, PBKDF2‑SHA256 600 000 vueltas) | RESPALDO‑1 y RESPALDO‑2 |
| `vigia-B.publica.txt` | solo la clave pública y su huella | TRASLADO (vuelve al PC de desarrollo) |

Anota en papel la huella que imprime (`huella: ....`).
Nunca existe B en claro en disco, ni sale por pantalla.

## 3. Respaldo de B — ⏸ PARADA: frase

1. Copia `vigia-B.respaldo.pem` y `vigia-B.publica.txt` a RESPALDO‑1 y a RESPALDO‑2.
2. Comprueba cada memoria (pide la frase):
   ```powershell
   .\VigiaFirma.exe comprobar-respaldo E:\vigia-B.respaldo.pem C:\VigiaFirma\claves\vigia-B.publica.txt
   ```
3. Guarda RESPALDO‑1 y RESPALDO‑2 en dos sitios distintos, y el papel con la frase en un tercero. Sin la frase el
   respaldo no sirve; sin el respaldo, perder este equipo es perder B.

## 4. Pasar A al equipo de firma — ⏸ PARADA: copia de una clave privada

Esto lo haces tú; ningún agente lo hace por ti.

1. En el PC de desarrollo, copia **a mano** `C:\Users\<tú>\.vigia\release-key.pem` a TRASLADO (Explorador o
   `Copy-Item` en tu propia consola).
2. En el equipo de firma:
   ```powershell
   .\VigiaFirma.exe importar E:\release-key.pem C:\VigiaFirma\claves A C:\VigiaFirma\vigia-A.publica.txt
   ```
   Comprueba que es la A esperada (si no, no importa nada) y pide una frase para el respaldo de A (puede ser otra).
   Deja `vigia-A.dpapi`, `vigia-A.respaldo.pem` y `vigia-A.publica.txt`. No cambia ni borra el `.pem`.
3. Copia `vigia-A.respaldo.pem` a RESPALDO‑1 y RESPALDO‑2 y compruébalo como en el paso 3.
4. Borra `release-key.pem` de TRASLADO (y vacía la papelera de esa memoria).
5. Cuando A esté importada y su respaldo comprobado, decide tú si borras la copia del PC de desarrollo. Recomendación:
   borrarla; mientras siga ahí, cualquier programa con tu cuenta puede leerla. Hazlo tú, no un agente:
   ```powershell
   Remove-Item "$env:USERPROFILE\.vigia\release-key.pem"
   ```

## 5. Llevar la pública B al PC de desarrollo (sin secretos)

1. En TRASLADO solo puede ir `vigia-B.publica.txt`. Comprueba antes de enchufarla en el PC de desarrollo:
   ```powershell
   Get-ChildItem E:\ -Recurse -Include *.dpapi,*.respaldo.pem,*.pem,*.key,*.pfx   # tiene que salir vacío
   ```
2. En el PC de desarrollo (esto lo puede hacer el agente):
   ```bash
   node tools/signing/preparar-relevo.js anadir /e/vigia-B.publica.txt
   ```
   Comprueba la huella, mete B en `NextKeys` de Puente y de Vigía, sube Puente de 2.0.1 a **2.0.2** y guarda la pública
   en `tools/signing/claves-publicas/`. **Compara la huella que imprime con la que anotaste en el paso 2.**
3. Commit, y candidato nuevo sin firmar:
   ```bash
   VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "transición A→B"
   node.exe tools/candidate-check.js out/0.21.0 < /dev/null   # tiene que decir «claves de relevo registradas (B …)»
   ```
   El candidato cambia (lleva B): sus huellas nuevas quedan en `out/0.21.0/candidato.txt` y en
   `RELEASE_CANDIDATE_0.21.0.md`. Las del candidato sin B dejan de valer.
4. Copia la carpeta `out/0.21.0` (las cuatro piezas) a TRASLADO, y **solo** `candidato.txt` a HOJA con el nombre
   `huellas-0.21.0.txt`. Apunta en papel los 8 primeros y 8 últimos caracteres de cada huella, leídos del informe.

## 6. Firmar la transición (en el equipo de firma) — ⏸ PARADA: firma con A

1. Compara `E:\HOJA\huellas-0.21.0.txt` con lo que apuntaste en papel (`Get-Content`).
2. Firma (no pide frase: usa los `.dpapi` de esta cuenta):
   ```powershell
   cd C:\VigiaFirma
   powershell -NoProfile -ExecutionPolicy Bypass -File .\firmar-fuera.ps1 `
     -Version E:\0.21.0 -Clave C:\VigiaFirma\claves\vigia-A.dpapi `
     -Publica C:\VigiaFirma\vigia-A.publica.txt -Huellas F:\huellas-0.21.0.txt `
     -ClaveRelevo C:\VigiaFirma\claves\vigia-B.dpapi
   ```
   Para en el primer problema y no deja firmas a medias:
   - las cuatro piezas tienen que ser exactamente las de la hoja (que llega aparte) → rc 4;
   - sin red → rc 5;
   - la clave solo la puede abrir esta cuenta → rc 3;
   - el Puente **de esta versión** tiene que aceptar una firma de prueba hecha con B; si no, no es una transición → rc 7;
   - firma con A y lo comprueba con otro programa (`VigiaFirma verificar`) usando **solo** la pública A, que es lo único
     que conocen las instalaciones 0.20.17; si no pasa, quita las firmas → rc 6.
3. `rc 0`: quedan los cuatro `.sig` y `firmas-0.21.0.txt` en `E:\0.21.0`. Nada más se escribe ahí.

## 7. Volver al PC de desarrollo y comprobar (sin secretos)

```bash
cp /e/0.21.0/*.sig /e/0.21.0/firmas-0.21.0.txt out/0.21.0/
VIGIA_RELEASE_SIGNED=1 VIGIA_RELEASE_DRY=1 bash tools/release.sh "transición A→B"
```
Con `VIGIA_RELEASE_DRY=1` comprueba firmas, manifiesto y que lo firmado es lo compilado (`candidato.txt`) y **para**: no
toca la red ni git. **Sin `VIGIA_RELEASE_DRY=1`, `VIGIA_RELEASE_SIGNED=1` publica** (sube a GitHub, etiqueta y hace push): eso
solo con tu aprobación de publicación.

## 8. Validación en VigiaLab del paquete firmado (sin secretos)

Con el paquete real firmado, en la VM (checkpoint `v02017-datos`), lo mismo que ya pasó con claves de laboratorio:
instalación desde cero; migración desde 0.20.17 (Puente 1.4.0 → 2.0.2, versión y SHA‑256 del nuevo Puente); rechazo de
firma inválida y de paquete alterado; rollback y downgrade; datos conservados; sin servicio SYSTEM; la interfaz no pide
elevación al abrir. Que el Puente real 2.0.2 acepta B lo prueba el paso 6 (`-ClaveRelevo`); la primera actualización
firmada con B de verdad es la del paso 9.

## 9. Retirar A (versión siguiente, firmada con B)

Cuando la transición lleve publicada el tiempo que decidas:
```bash
node tools/signing/preparar-relevo.js promover     # B única de confianza, Puente 2.0.2 -> 2.0.3
```
Candidato nuevo (paso 5.3‑5.4) y firma en el equipo de firma con B:
```powershell
... firmar-fuera.ps1 -Version E:\<versión> -Clave C:\VigiaFirma\claves\vigia-B.dpapi `
      -Publica C:\VigiaFirma\claves\vigia-B.publica.txt -Huellas F:\huellas-<versión>.txt
```
Desde esa versión, lo firmado con A se rechaza. Quien siga en 0.20.x (solo confía en A) ya no podrá actualizar solo:
tendrá que reinstalar a mano (Recuperación). Después de publicarla, A ya no hace falta: guarda o destruye sus respaldos
según decidas; `vigia-A.dpapi` puede borrarse del equipo de firma.

## Numeración de Puente

| Puente | Confía en | Estado |
|---|---|---|
| 1.4.0 | A | publicado (0.20.16, 0.20.17) |
| 2.0.1 | A | **nunca publicado** (candidato actual sin B); no se publicará si 0.21.0 es la transición |
| 2.0.2 | A + B | transición (0.21.0) |
| 2.0.3 | B | retirada de A |

Los Puente 2.0.1/2.0.2/2.0.3 de los paquetes de laboratorio (`scratch/lab`, claves de laboratorio) no se distribuyen.
`tools/release.sh` se niega a publicar un Puente con código cambiado y el mismo número que el último publicado.

## Que el PC de desarrollo no tiene B

- B solo existe como `vigia-B.dpapi` (DPAPI de la cuenta del equipo de firma: copiado a otro equipo no se puede abrir) y
  como respaldo cifrado con frase en RESPALDO‑1/2, que nunca se enchufan al PC de desarrollo.
- `tools/candidate-check.js` falla si el zip lleva `.pem/.key/.pfx/.p12/.snk/.dpapi` o bloques PEM privados, y si
  hay una clave de relevo que no esté registrada en `tools/signing/claves-publicas/`.
- Comprobación en el PC de desarrollo (rc 0 = no hay; solo lista rutas, nunca contenido):
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\sin-clave-b.ps1
  ```
  Busca `vigia-*.dpapi` y `vigia-*.respaldo.pem` en tu perfil y en `C:\ProgramData` (otras carpetas: `-Raiz`).
  31‑10‑2026: rc 0, ninguna.

## Recuperación (instalación por canal de confianza)

Para quien se haya quedado en 0.20.x tras retirar A, o sospeche de una actualización falsa:
1. Descargar `Vigia-Setup.exe`, `Vigia-Setup.exe.sig` y `vigia-manifest.json(.sig)` de la página de la versión en GitHub.
2. Comprobar el SHA‑256 de `Vigia-Setup.exe` contra la huella publicada **por otro canal** (notas de la versión y, aparte,
   el README del repositorio o un mensaje tuyo firmado), y la huella de la clave pública B
   (`tools/signing/claves-publicas/vigia-B.publica.txt`) contra la que tú anotaste.
3. Comprobar la firma con un programa distinto del instalador:
   `VigiaFirma.exe verificar <carpeta-con-las-piezas> vigia-B.publica.txt`.
4. Instalar. Los datos de usuario se conservan (los guarda la instalación; probado en VigiaLab con 135 archivos).

## Prueba real de la clave B con el agente de Bitwarden (31-10-2026, 04:42 UTC)

- Lanzada por el propietario en su PowerShell (`VigiaFirma prueba-agente`), con aprobación manual en Bitwarden. Desde Hermes la misma orden se niega antes de pedir nada (comprobado).
- Texto firmado: `vigia-prueba-agente 1` + fecha + azar; SHA-256 `02cf003ff2b3d11928e8501a4eb9c30c5bd7df897e8f5f41f68d6d475aa13645` (87 bytes). Prefijo de prueba: Puente no lo acepta como firma de una versión.
- Clave: `SHA256:jBQLJ8JYVIDMJorrwJe8TYPsTrKiGN+WcYWu0fS1G3c` (vigia-B.publica.txt).
- Verificado tres veces por separado: VigiaFirma (verificador propio), `verificar-b.js` (OpenSSL, rc 0) y `Puente-test --ed25519-raw` (rc 0). El mismo texto con un bit cambiado no verifica (OpenSSL).
- Esto demuestra que Bitwarden firma con B en el formato que Puente 2.0.2 verifica. No es una firma de versión: la 0.21.0 sigue sin firmar.
