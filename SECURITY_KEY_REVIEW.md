# Revisión de la clave de firma — 2026-10-29

Clave: `%USERPROFILE%\.vigia\release-key.pem` (ECDSA P-256, PKCS#8, sin cifrar). Es la que firma las versiones que
instala Puente. **No se abrió para mostrarla, no se copió, no se cambió, no se rotó.** Las comparaciones contra otros
archivos se hicieron leyendo la clave en memoria y pasando trozos de su parte aleatoria por una tubería a `rg`
(nada escrito en disco salvo un archivo temporal con ACL solo de tu cuenta, borrado al terminar).

Estados: `IMPLEMENTED` código hecho · `UNIT_TESTED` pruebas automáticas · `BLOCKED` falta algo · `REJECTED` descartado.

## Veredicto

**POSIBLE EXPOSICIÓN (sin evidencia de lectura).** La clave estuvo y sigue estando legible por las dos cuentas del
sandbox de Codex, una de ellas con red. No hay rastro de que ninguna la leyera ni de que esté copiada en ningún sitio,
pero no se puede demostrar que no ocurrió. Hasta que decidas: no se firma con ella (lo impide el código, ver abajo).

## Investigación (los 11 puntos)

| # | Punto | Resultado | Evidencia |
| --- | --- | --- | --- |
| 1 | Propietario | `DESKTOP-F8CBGOK\Pickle OS` | `Get-Acl … .Owner` |
| 2 | ACL efectiva | `CodexSandboxUsers`: ReadAndExecute · SYSTEM, Administradores, Pickle OS: control total | `Get-Acl … .Access` |
| 3 | Quién puede leer | Tu cuenta, SYSTEM, Administradores y **`CodexSandboxUsers`** = `CodexSandboxOffline` + `CodexSandboxOnline` | `net localgroup CodexSandboxUsers` |
| 4 | Herencia | El archivo no tiene ACL propia (`AreAccessRulesProtected=False`): todo es heredado. La carpeta `.vigia` tiene la entrada de Codex **explícita** (no heredada) `(OI)(CI)(RX)` | `Get-Acl` de archivo y carpeta |
| 5 | Cuenta de Hermes | `Pickle OS` (administrador; en este PC UAC está desactivado, así que corre con permisos completos) | `whoami` |
| 6 | Cuenta de Codex | `codex exec` lo lanzó Hermes, o sea `Pickle OS`. Las cuentas `CodexSandboxOffline`/`CodexSandboxOnline` existen y son del sandbox de Codex; que los comandos del sandbox corran con ellas se deduce de sus nombres y del grupo, no se observó un proceso en marcha | `net user`, `net localgroup` |
| 7 | Cuenta de `release.sh` | La que lo lance: hoy `Pickle OS` (git-bash) | `tools/release.sh` |
| 8 | Lectura desde procesos no autorizados | **Sí, posible.** Cualquier proceso de `Pickle OS` (incluidos Hermes, subagentes, compilaciones, Codex fuera del sandbox) y los del sandbox de Codex. `CodexSandboxOnline` tiene red | ACL de los puntos 2–4 |
| 9 | En Git | **No.** Ningún `.pem`/`.key` en ninguna rama; ningún trozo de la parte aleatoria de la clave en `git log --all -p` | `rg -F -f` sobre `git log --all -p`: 0 coincidencias |
| 10 | En copias, logs, artefactos | **No encontrada** en `%LOCALAPPDATA%\hermes`, `~\.codex`, `~\work` (repo, `out/`, `vigia-wt/_prompts`), `%TEMP%`, Escritorio, Documentos, Descargas. Dos coincidencias iniciales (`vapid.json` de pruebas `pytest` de otro proyecto, en `%TEMP%`) eran la **cabecera estándar** de toda clave P-256 PKCS#8; con solo la parte aleatoria: 0 | `rg -l -F -f` (archivos ≤50 MB) |
| 11 | Exposición histórica | Ventana: desde que existe la entrada de Codex en `.vigia` hasta hoy. Clave creada el 27-10-2026. Las ejecuciones de Codex de este proyecto fueron con `--ephemeral` (sin sesiones guardadas: 0 sesiones nombran la clave). Sus logs en `vigia-wt/_prompts` solo nombran la ruta dentro de diffs que se le pasaron, no lecturas. La «última lectura» del archivo (29-10 12:34) son las comprobaciones de esta revisión, así que no sirve para descartar lecturas anteriores | logs y `LastAccessTime` |

Otros archivos en `%USERPROFILE%\.vigia` con la misma ACL: `vm-cred.txt` y `host-backup\`. **No se abrieron.**

### Qué limita el daño si alguien la tuviera

* Puente y Vigía solo descargan de `github.com/JcperezDev/vigia` (rutas fijas en `Updates.cs` y `Program.cs` de
  Puente). Con la clave sola no basta para llegar a los usuarios por actualización: también haría falta poder publicar
  en ese repositorio.
* Lo que sí permitiría: firmar un paquete que pase por bueno con `Puente.exe --offline <carpeta>` o un `Puente.exe`
  falso que Vigía aceptaría como actualización si se lo entregaran por otra vía.

## Mitigaciones implementadas (sin tocar la clave)

| Cambio | Estado | Archivos | Prueba |
| --- | --- | --- | --- |
| `--sign` y `--sign-release` **se niegan** (código 3) si la clave la puede abrir alguien que no sea tu cuenta, SYSTEM o Administradores, si el dueño es otro o si es un enlace | IMPLEMENTED, UNIT_TESTED | `app/Puente/Setup.cs` (`KeyAclProblem`), `Program.cs` | puente-test «no se firma con una clave que otro grupo puede leer» |
| `Puente.exe --key-check <clave>`: dice si está protegida, sin abrirla | IMPLEMENTED, UNIT_TESTED | `Setup.cs` (`KeyCheck`) | puente-test; sobre la clave real: `Clave NO protegida: … CodexSandboxUsers (heredado de la carpeta)`, código 3 |
| `--genkey` y `--protect-key` crean el archivo con ACL propia (solo tu cuenta, sin herencia) | IMPLEMENTED, UNIT_TESTED | `Program.cs` (`GenKey`), `Setup.cs` (`WriteOwnOnly`) | puente-test «--genkey crea la clave con permisos propios» |
| **Firma fuera del entorno de desarrollo**: `VIGIA_RELEASE_SIGN=fuera` compila, prueba y arma sin tocar la clave; `Puente.exe --sign-release <carpeta> <clave>` en la cuenta/PC de firma firma las 4 piezas, comprueba el manifiesto con la clave pública compilada, no pisa firmas y quita lo firmado si algo no cuadra; `VIGIA_RELEASE_SIGNED=1` publica lo firmado fuera sin necesitar la clave | IMPLEMENTED, UNIT_TESTED | `tools/release.sh`, `Setup.cs` (`SignRelease`) | puente-test (6 casos), release-test 8b (4 casos) |
| `release.sh` ya no busca la clave cuando firma fuera o publica lo firmado | IMPLEMENTED, UNIT_TESTED | `tools/release.sh` | release-test 8b |
| Documentación | IMPLEMENTED | `docs/SIGNING.md` | — |

Resultados: Puente **74 OK / 0** (antes 64), release.sh **31 OK / 0** (antes 27). Las pruebas usan claves de prueba
creadas en `%TEMP%` con `--genkey`; la clave real no se usa.

### DPAPI

`--protect-key` (ya existía) cifra la clave con la cuenta de Windows. Protege frente a otras cuentas (el sandbox de
Codex no podría descifrarla aunque copiara el archivo) y frente a una copia del archivo en otro PC. **No protege** frente
a cualquier proceso de `Pickle OS`, que puede descifrarla igual que `release.sh`. Es una mejora básica, no la solución.

## Diseño recomendado

1. **Ya**: quitar la entrada de Codex de `.vigia` (comando abajo). Con eso `--key-check` pasa a «protegida».
2. **Cuenta de firma aparte** (local, estándar, sin navegador ni herramientas de desarrollo) o un PC/VM sin red. La
   clave vive ahí cifrada con DPAPI de esa cuenta; tu cuenta normal no puede leerla. Se firma con
   `--sign-release` bajo demanda; ni Hermes, ni subagentes, ni compilaciones, ni Codex corren en esa cuenta.
3. Copia offline de la clave (USB cifrado / gestor de contraseñas), para no perderla si se reinstala Windows.
4. Más adelante: token hardware (PIV P-256) para que la clave no se pueda exportar. Requiere ruta CNG en Puente: no
   implementado.
5. No usar secretos de CI: quien cambie un workflow podría sacarlos.

## Acciones que necesitan tu autorización (no hechas)

| Acción | Comando / pasos | Por qué no lo hice |
| --- | --- | --- |
| Quitar lectura de Codex a `.vigia` | `icacls "%USERPROFILE%\.vigia" /remove:g CodexSandboxUsers /t` y comprobar con `Puente.exe --key-check` | Cambia permisos en tu Windows principal y puede afectar a Codex |
| Cifrar con DPAPI | `docs/SIGNING.md` → «Moving from .pem to .dpapi» | Toca cómo está guardada la clave |
| Mover la clave a una cuenta de firma | crear cuenta local, `--protect-key` allí, copia offline, borrar el `.pem` de tu cuenta | Crea una cuenta y borra un archivo |
| Rotar la clave | procedimiento abajo | Prohibido sin autorización |

## Procedimiento de rotación de confianza (preparado, NO ejecutado)

Decidir con este criterio: si se quita la entrada de Codex y no aparece ninguna señal de abuso (versiones o firmas que
no reconozcas), se puede **no rotar** y dejarlo como riesgo aceptado. Si prefieres no aceptar el riesgo, rotar así:

1. En la cuenta/PC de firma nueva: `Puente.exe --genkey nueva.pem` (imprime la clave pública), luego `--protect-key`,
   y copia offline.
2. **Falta código** (no implementado): que Puente y Vigía confíen en **dos** claves públicas a la vez (la vieja y la
   nueva). Es un cambio pequeño en `Program.cs` (`PublicKey`) y `Updates.cs`, con pruebas en puente-test.
3. Versión **puente** N firmada con la clave **vieja**, cuyos binarios confían en las dos. Los usuarios actualizan
   normal.
4. Esperar varias semanas a que casi todos estén en N.
5. Versión N+1 firmada con la **nueva**; sus binarios confían solo en la nueva. La vieja se guarda offline y no se usa.
6. Si hubiera señales de abuso: en N subir `minVersion` del manifiesto a N, avisar en el README y en las notas para que
   quien instale a mano baje Puente de nuevo.

## Próximo paso

Tu decisión sobre la entrada de Codex en `.vigia`. Mientras tanto `release.sh` no firmará con la clave actual (eso es lo
esperado: «debe resolverse antes de publicar»).
