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

Estado de publicación: **`SIGNING_BLOCKED`** mientras siga la entrada de `CodexSandboxUsers` en `.vigia` y la clave viva
en la misma cuenta donde corren los agentes de programación (ver «Reauditoría» al final).

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
2. **Hecho en código (sin claves nuevas):** Puente (`Program.cs`, `NextKeys` + `VerifyWith`) y Vigía (`Updates.cs`,
   `PuenteSigOk`) aceptan la clave actual **o** cualquiera de la lista `NextKeys`, que hoy va **vacía** (nada cambia
   hasta rotar). Para rotar se pega en las dos listas la clave **pública** nueva. Probado con claves de prueba:
   puente-test «relevo de clave» (con la nueva en la lista se instala; sin ella se rechaza) y selftest (3 casos, incluido
   «esta versión no trae claves de relevo»).
3. Versión **puente** N firmada con la clave **vieja**, cuyos binarios confían en las dos. Los usuarios actualizan
   normal.
4. Esperar varias semanas a que casi todos estén en N.
5. Versión N+1 firmada con la **nueva**; sus binarios confían solo en la nueva. La vieja se guarda offline y no se usa.
6. Si hubiera señales de abuso: en N subir `minVersion` del manifiesto a N, avisar en el README y en las notas para que
   quien instale a mano baje Puente de nuevo.

### Compatibilidad y protección contra volver atrás (ya en el código)

- **Instalaciones antiguas (0.20.x):** su Puente solo conoce la clave actual. Por eso la versión puente N va firmada con
  la vieja; si se saltara ese paso, un 0.20.x rechazaría N+1 (falla seguro: no instala, no se rompe) y habría que
  instalar a mano.
- **Puente nunca baja de versión:** si el Puente instalado es más nuevo que el del manifiesto, se queda
  (`Setup.cs`, `have >= m.PuenteVersion`). Un manifiesto viejo firmado no puede traer de vuelta un Puente vulnerable.
- **Vigía nunca baja de versión por manifiesto:** un manifiesto más viejo que la versión más alta instalada se rechaza
  («un manifiesto viejo no puede volver atrás»), probado en VigiaLab (0.21.1 → 0.21.0, rc 1, sin cambios). Volver a
  la anterior solo existe como acción local y explícita del usuario sobre una carpeta ya verificada.
- **Revocación lógica:** quitar una clave de `PublicKey`/`NextKeys` en una versión nueva hace que esa versión deje de
  aceptar lo firmado con ella. No hay revocación remota (no hay servidor; es a propósito).

## Reauditoría (29-10-2026, tarde) — sin abrir la clave

| Comprobación | Resultado | Evidencia |
| --- | --- | --- |
| ACL efectiva hoy | Igual: `CodexSandboxUsers:(I)(RX)` heredado de `.vigia`, donde es **explícito** `(OI)(CI)(RX)`. SYSTEM, Administradores y `Pickle OS`: control total. Dueño `Pickle OS` | `icacls`, `Get-Acl` |
| Dónde más tiene Codex lectura | Entradas explícitas en `~\work` y `~\AppData` (y heredadas debajo, incluido el scratch con la clave de laboratorio). No en la raíz del perfil | `icacls` |
| Cuentas del grupo | `CodexSandboxOffline` (último inicio de sesión 29-10 13:37) y `CodexSandboxOnline` (nunca) | `Get-LocalUser`, eventos 4624 de hoy: 8 inicios de `CodexSandboxOffline` entre 11:51 y 13:37, probablemente las revisiones de código de solo lectura lanzadas hoy (no cuadrado al minuto); ninguno de `CodexSandboxOnline` |
| Auditoría de acceso a archivos | **Desactivada** (`auditpol`: File System = No Auditing). Windows no guarda quién abrió el archivo | `auditpol /get` |
| Fecha de último acceso | Activada (`DisableLastAccess = 0`). Prueba con archivos de control: `icacls`, `Get-Acl` y `Get-Item` **no** la cambian; leer el contenido **sí**, y también `ls`/`stat` de la consola (abren el archivo). Último acceso de la clave: 29-10 18:55:32, y antes 18:00:09: coinciden al segundo con dos listados `ls -la ~/.vigia` de esta misma revisión | `scratch/p9/atime*.ps1`, historial de comandos |
| Procesos del sandbox ahora | Ninguno | `Win32_Process` + dueño |
| Índice de búsqueda de Windows | `.vigia` sin entradas | consulta al índice, solo rutas |
| Referencias en código y scripts | Solo `tools/release.sh` (busca `.dpapi` y si no `.pem`) y la entropía DPAPI de `Native.cs`; los scripts de laboratorio usan `lab.pem` (ficticia) y nunca la ruta real | `grep -rn release-key` |

Conclusión: **exposición potencial por permisos, sin evidencia de acceso efectivo por otra cuenta.** Los accesos que se
ven los explica esta revisión. Como no hay auditoría de archivos y la fecha de acceso se pisa, tampoco se puede
demostrar lo contrario. No se afirma robo.

Además del sandbox de Codex, **cualquier proceso de `Pickle OS` puede leer la clave**, incluido Hermes y sus
subagentes (lo demuestra que un `ls` de esta revisión tocó su fecha de acceso). Quitar la entrada de Codex cierra la
vía de las otras cuentas, pero no esta. DPAPI con la misma cuenta tampoco la cierra. La única solución que impide que un
agente de programación llegue a la clave de producción es que **la clave no esté en esta cuenta**: cuenta de firma
aparte o PC/VM sin red, firmando con `Puente.exe --sign-release` (ya implementado) y `VIGIA_RELEASE_SIGN=fuera`.

### Paso exacto que necesita tu autorización

1. Quitar la lectura de Codex: `icacls "%USERPROFILE%\.vigia" /remove:g CodexSandboxUsers` y comprobar con
   `Puente.exe --key-check "%USERPROFILE%\.vigia\release-key.pem"` (debe decir «Clave protegida», código 0).
2. Elegir: (a) aceptar el riesgo y mover la clave a una cuenta de firma, o (b) rotar con el relevo de claves de arriba.
   Recomendado: **(b)**, porque la ventana de exposición no se puede cerrar con pruebas y el relevo ya está hecho; la
   rotación cuesta una versión intermedia.
3. Tras mover o rotar, borrar el `.pem` de esta cuenta (borrado tuyo, no de un agente).

Hasta entonces: `SIGNING_BLOCKED`.

## Revisión de metadatos (29-10-2026, noche) — sin abrir la clave

Solo `Get-Item` y `Get-Acl` (la prueba de arriba demostró que no tocan la fecha de acceso). No se leyó el contenido.

| Dato | Valor |
| --- | --- |
| Tamaño | 240 bytes (igual que antes) |
| Creada / modificada (UTC) | 27-10-2026 23:03:06 / 27-10-2026 23:03:06: **no ha cambiado desde que se creó** |
| Atributos | `Archive` (no oculta ni de solo lectura) |
| Dueño | `DESKTOP-F8CBGOK\Pickle OS` |
| ACL del archivo | Toda heredada: `CodexSandboxUsers` ReadAndExecute · SYSTEM, Administradores y `Pickle OS` control total |
| ACL de `.vigia` | `CodexSandboxUsers` ReadAndExecute **explícita**; el resto heredado |

Sigue igual que en la reauditoría de la tarde: **`SIGNING_BLOCKED`**.

## Procedimiento exacto (para cuando lo autorices) — NO ejecutado

Todo en **tu** consola (`cmd` como `Pickle OS`), no en una sesión de un agente. Ningún paso abre ni muestra la clave.

### A. Retirar el acceso de `CodexSandboxUsers` (reversible)

```bat
:: 0. Que no haya sesiones del sandbox en marcha
tasklist /v | findstr /i CodexSandbox
:: 1. Guardar la ACL actual de la carpeta y lo que tiene dentro (para volver atrás)
icacls "%USERPROFILE%\.vigia" /save "%USERPROFILE%\vigia-acl-antes.txt" /t /c
:: 2. Quitar la entrada explícita de la carpeta; el archivo la heredaba, así que también se va de él
icacls "%USERPROFILE%\.vigia" /remove:g "DESKTOP-F8CBGOK\CodexSandboxUsers" /t /c
:: 3. Comprobar (sin abrir la clave): debe decir «Clave protegida», código 0
"%USERPROFILE%\work\vigia\app\Puente\bin\Release\net10.0\win-x64\Puente.exe" --key-check "%USERPROFILE%\.vigia\release-key.pem" & echo rc=%errorlevel%
icacls "%USERPROFILE%\.vigia\release-key.pem"
```

(El Puente 0.20.17 instalado no tiene `--key-check`; se usa el compilado de `debloat-v2`.)

Volver atrás: `icacls "%USERPROFILE%" /restore "%USERPROFILE%\vigia-acl-antes.txt"` (el archivo guardado usa rutas
relativas a la carpeta padre). Efecto conocido: Codex en modo sandbox deja de leer `.vigia`; no usa nada de ahí.

Lo que **no** arregla: cualquier proceso de `Pickle OS` (Hermes, subagentes, compilaciones) sigue pudiendo leerla. Por
eso `SIGNING_BLOCKED` se mantiene hasta B.

### B. Custodia con una identidad de firma aislada

1. Crear la cuenta local estándar `vigia-firma` (sin Microsoft Account, sin navegador ni herramientas de desarrollo,
   contraseña larga en tu gestor de contraseñas): `net user vigia-firma * /add` (pide la contraseña sin mostrarla).
2. Carpeta de firma con ACL propia: `mkdir C:\VigiaFirma` y
   `icacls C:\VigiaFirma /inheritance:r /grant:r "vigia-firma:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "Administrators:(OI)(CI)F"`.
3. Copia offline cifrada del `.pem` (USB cifrado o adjunto del gestor de contraseñas), hecha por ti. Nunca a `%TEMP%`
   ni a una carpeta compartida.
4. **Como `vigia-firma`** (`runas /user:vigia-firma cmd`), desde la copia offline:
   `Puente.exe --protect-key <USB>\release-key.pem C:\VigiaFirma\release-key.dpapi` (o una clave nueva con
   `Puente.exe --genkey`, ver C). DPAPI la ata a esa cuenta: `Pickle OS` ya no puede descifrarla.
5. Borrar el `.pem` de `Pickle OS` tú mismo, tras comprobar que en `vigia-firma` se puede firmar una carpeta de prueba
   (`Puente.exe --sign-release <carpeta de prueba> C:\VigiaFirma\release-key.dpapi`).
6. Publicar desde entonces así: en tu cuenta `VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "notas"` (compila, prueba,
   arma, no toca la clave) → copia `out\<versión>` a una carpeta de intercambio (`C:\VigiaEntrega`, sin la clave) → en
   `vigia-firma` `Puente.exe --sign-release C:\VigiaEntrega\<versión> C:\VigiaFirma\release-key.dpapi` → devuelve los
   cuatro `.sig` a `out\<versión>` → en tu cuenta `VIGIA_RELEASE_SIGNED=1 bash tools/release.sh "notas"` (comprueba y publica).

Volver atrás: la copia offline del paso 3 y `net user vigia-firma /delete`.

### C. Rotación: evaluación

| Opción | Qué pasa con las instalaciones existentes | Riesgo que queda |
| --- | --- | --- |
| **No rotar** (solo A + B) | Nada cambia: 0.20.x actualiza a 0.21.0 normal | Si la clave ya se copió, quien la tenga puede firmar paquetes que pasen por buenos fuera de GitHub (ver «Qué limita el daño»). No se puede descartar con pruebas |
| **Rotar con relevo** (recomendado) | 0.21.0 se firma con la clave **vieja** y lleva la pública **nueva** en `NextKeys` (Puente y Vigía). 0.20.17 la acepta. Desde 0.21.1 se firma con la nueva | Las 0.20.x que no actualicen nunca solo confían en la vieja. 0.21.0 sigue firmada con la vieja (inevitable: es lo único que 0.20.x verifica) |
| Rotar sin relevo | 0.20.x rechaza todo lo nuevo (falla seguro) y hay que reinstalar a mano | Rompe la actualización de quien ya la tiene: **descartado** |

Si eliges rotar, lo único que cambia en este repositorio es pegar la clave **pública** nueva en `NextKeys` de
`app/Puente/Program.cs` y `app/Vigia/Updates.cs` antes de compilar 0.21.0, y cambiar la prueba
«esta versión no trae claves de relevo». La privada nueva se crea directamente en `vigia-firma` (B.4 con `--genkey`).

## Comprobación y análisis adicional (30-10-2026) — sin abrir la clave

Metadatos de hoy (`Get-Item`, `Get-Acl`; no se leyó el contenido):

| Dato | Valor |
| --- | --- |
| Tamaño / creada / modificada (UTC) | 240 bytes · 27-10-2026 23:03:06 · 27-10-2026 23:03:06 (sin cambios) |
| ACL del archivo | heredada: `CodexSandboxUsers` ReadAndExecute · SYSTEM, Administradores, `Pickle OS` control total |
| ACL de `.vigia` | `CodexSandboxUsers` ReadAndExecute **explícita** |
| ACL del perfil | SYSTEM, Administradores, `Pickle OS` (+ dos SID de capacidad de apps con solo `ExecuteFile`) |
| Procesos de cuentas `CodexSandbox*` ahora | 0 |
| UAC en el host | **`EnableLUA = 0`** (desactivado) |
| Administradores locales | `Administrator`, **`Pickle OS`** |

### Lo que cambia del diseño B: una cuenta de firma en este mismo PC no basta

Con `Pickle OS` como administrador y UAC desactivado, **todo** proceso de esa cuenta (Hermes, subagentes, compilaciones,
Codex fuera del sandbox) corre con permisos de administrador. Un administrador puede tomar posesión de los archivos de
otra cuenta, leer su memoria mientras firma, instalar algo que se ejecute en su sesión o cambiar `Puente.exe` antes de
que esa cuenta lo use. DPAPI de `vigia-firma` impide copiar el `.dpapi` y descifrarlo en frío, pero **no** protege
mientras la cuenta de firma inicia sesión en una máquina que controla un administrador comprometido.

Por eso el orden recomendado queda así:

1. **Equipo de firma separado y sin red** (otro PC, o una VM que solo arranca para firmar, con disco cifrado, sin
   carpetas compartidas y sin agentes): recibe `out\<versión>` por USB, ejecuta `Puente.exe --sign-release` y devuelve
   los cuatro `.sig`. El `Puente.exe` que firma se copia una vez, con su SHA-256 anotado, y no se actualiza desde este PC.
2. **Token hardware** (PIV P-256): la clave no se puede exportar. Necesita una ruta CNG/PKCS#11 en `--sign-release`
   que **no está implementada** (no hay token para probarla).
3. **Cuenta de firma local (B)**: solo es aceptable si la cuenta de desarrollo deja de ser administradora o vuelve a tener
   UAC activo. Así se reduce el riesgo, pero esa cuenta no queda aislada de verdad.

Activar UAC o quitarle el administrador a `Pickle OS` es un cambio del Windows principal: **no se hizo** y necesita
tu autorización.

### Si alguien ya tuviera la clave actual

* Podría firmar un `vigia-manifest.json`, un zip y un `Puente.exe` que las instalaciones 0.20.x y 0.21.x aceptarían
  **si se los entregan**. Puente y Vigía solo descargan de `github.com/JcperezDev/vigia` y con HTTPS, así que para llegar
  por actualización automática también necesitaría publicar en ese repositorio (o sea, tu cuenta de GitHub).
  Por fuera (`Puente.exe --offline <carpeta>` o un `Vigia-Setup.exe` de otra web) sí funcionaría.
* El relevo de claves (`NextKeys`) **no lo detiene durante la transición**: mientras una versión confíe en la clave
  vieja, lo firmado con ella sigue valiendo. Por eso la ventana se cierra cuando los usuarios estén en una versión que
  ya no confía en la vieja (paso 5 del procedimiento de rotación), no antes.
* No hay revocación remota (no hay servidor, a propósito). La revocación es «una versión nueva que ya no confía en la
  clave»; las instalaciones que nunca actualizan siguen confiando en ella.

### Instalaciones con actualizadores antiguos

| Instalación | Qué pasa si se rota con relevo | Cómo se recupera |
| --- | --- | --- |
| 0.20.17 (Puente 1.x/2.0.0, solo conoce la clave vieja) | Acepta 0.21.0 firmada con la vieja, que trae la nueva en `NextKeys` | Actualiza normal; desde ahí confía en las dos y después solo en la nueva |
| 0.20.x que se salta 0.21.0 y llega a 0.21.1+ (firmada con la nueva) | Rechaza (falla seguro: no instala, no rompe lo que hay) | Instalar a mano `Vigia-Setup.exe` de la página de releases; los datos de `%LOCALAPPDATA%\Vigia` se conservan (migración probada en VigiaLab) |
| Cualquiera, si la clave vieja se usó para un paquete malicioso fuera de GitHub | Lo aceptaría vía `--offline` | Aviso en README y notas; subir `minVersion` en la versión puente; reinstalar desde GitHub |

### ¿Hace falta rotar?

La ventana de exposición no se puede cerrar con pruebas (no hay auditoría de archivos y la fecha de acceso se pisa),
la clave tiene tres días y solo hay una versión pública firmada con ella (0.20.17). La rotación cuesta poco **ahora**:
0.21.0 aún no se publicó y puede ser la versión puente. Recomendación técnica: **rotar con relevo en 0.21.0**, con la
clave nueva creada directamente en el equipo de firma (opción 1). Es una decisión tuya; no se ejecutó nada.

Estado: **`SIGNING_BLOCKED`** (sin cambios).

## Próximo paso

Tu decisión sobre la entrada de Codex en `.vigia`. Mientras tanto `release.sh` no firmará con la clave actual (eso es lo
esperado: «debe resolverse antes de publicar»).
