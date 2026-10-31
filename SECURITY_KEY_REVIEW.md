# Revisión de la clave de firma — 2026-10-29

Clave: `%USERPROFILE%\.vigia\release-key.pem` (ECDSA P-256, PKCS#8, sin cifrar). Es la que firma las versiones que
instala Puente. **No se abrió para mostrarla, no se copió, no se cambió, no se rotó.** Las comparaciones contra otros
archivos se hicieron leyendo la clave en memoria y pasando trozos de su parte aleatoria por una tubería a `rg`
(nada escrito en disco salvo un archivo temporal con ACL solo de tu cuenta, borrado al terminar).

Estados: `IMPLEMENTED` código hecho · `UNIT_TESTED` pruebas automáticas · `BLOCKED` falta algo · `REJECTED` descartado.

## Veredicto

**Actualizado 30-10-2026 18:00 — POSIBLE EXPOSICIÓN ANTERIOR (sin evidencia de lectura) · acceso del sandbox a `.vigia` retirado.**
Hasta el 30-10 17:35 la clave fue legible por las dos cuentas del sandbox de Codex, una de ellas con red. No hay rastro
de que ninguna la leyera ni de que esté copiada en ningún sitio, pero no se puede demostrar que no ocurrió, y quitar el
permiso ahora no lo cambia. Además la clave sigue sin cifrar en la cuenta de desarrollo, que es administradora con UAC
desactivado: cualquier proceso de esa cuenta puede abrirla.

Estado de publicación: **`SIGNING_BLOCKED`** hasta que la clave con la que se firme esté fuera de este PC (o se rote
desde un equipo de firma). No se firma ninguna release de producción mientras tanto.

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

## Aislamiento definitivo y relevo: herramientas listas (30-10-2026) — NO ejecutadas sobre la clave real

Nada de esto tocó la clave real ni `~/.vigia`. Todo se probó con claves de prueba creadas y borradas en una carpeta
temporal: `node tools/signing/signing-test.js out/puente-test/Puente-test.exe` → **36 OK / 0 FAIL**.

### Hallazgo nuevo (solo permisos, sin abrir nada)

| Carpeta | Entrada de `CodexSandboxUsers` |
| --- | --- |
| `C:\Users\Pickle OS` | ninguna |
| `AppData` | lectura y ejecución, **explícita** (heredan `Local`, `Roaming` y todo lo de dentro) |
| `AppData\Local\Temp` | **Modificar**, explícita; además un SID sin nombre `S-1-5-21-…-3224999056` con Modificar |
| `.vigia` | lectura y ejecución, explícita (lo ya conocido) |

En el equipo existen las cuentas locales `CodexSandboxOffline` y `CodexSandboxOnline` (habilitadas). Por eso quitar
solo la entrada explícita de `.vigia` **no bastaba**: si algún día la clave pasa por `AppData` o `Temp`, ese grupo la
podría leer por herencia. La herramienta nueva corta la herencia en la carpeta de la clave. Lo de `AppData` y `Temp`
(que afecta a otros programas) no se toca: es decisión tuya.

### 1. Corregir permisos de `.vigia` (reversible) — `tools/signing/quitar-acceso-sandbox.ps1`

En tu consola, sin agentes abiertos:

```powershell
cd C:\Users\Pickle OS\work\vigia
powershell -NoProfile -ExecutionPolicy Bypass -File tools\signing\quitar-acceso-sandbox.ps1 -Puente app\Puente\bin\Release\net10.0\win-x64\Puente.exe
```

Qué hace: guarda una copia de los permisos actuales (`vigia-acl-antes-<fecha>.acl`, al lado de `.vigia`), corta la
herencia de `.vigia`, deja solo tu cuenta, SYSTEM y Administradores, hace que lo de dentro herede solo de ahí, comprueba
que no queda ninguna otra cuenta y termina con `Puente --key-check` (mira permisos, no abre la clave). No lee, no copia
ni cambia la clave. Códigos: 0 bien · 1 error · 3 la clave sigue abierta a alguien.
Volver atrás: `… quitar-acceso-sandbox.ps1 -Deshacer <archivo .acl>`.
Probado (claves de prueba): antes → `--key-check` rc 3; después → rc 0; los bytes de la clave no cambian; un permiso
nuevo en la carpeta de arriba ya no llega; deshacer devuelve el rc 3; no pisa una copia que ya existe.

**Esto reduce el riesgo pero no lo cierra**: con UAC desactivado (`EnableLUA = 0`) y tu cuenta administradora,
cualquier programa que corra como tú puede leer la clave. Por eso el paso 2.

### 2. Aislamiento definitivo: la clave sale de este PC — `tools/signing/firmar-fuera.ps1`

1. Equipo de firma: otro PC, o un portátil o USB arrancable, **sin red** y sin agentes ni herramientas de desarrollo.
2. Copia allí `Puente.exe` desde un candidato y apunta su SHA-256 (`Get-FileHash`) en papel o en el móvil.
3. **Si apruebas rotar (recomendado):** genera allí la clave nueva (`Puente.exe --genkey C:\VigiaFirma\release-key.pem`,
   que imprime la clave **pública**) y cífrala (`Puente.exe --protect-key … release-key.dpapi`; borra el `.pem`).
   **Si no rotas:** mueve la clave actual a ese equipo (USB que después formateas) y bórrala de `~/.vigia`. Esto es
   mover la clave real: solo con tu autorización.
4. En el PC de desarrollo: `VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "<notas>"` → carpeta `out\<versión>` sin firmar.
5. Llévala al equipo de firma y ejecuta
   `firmar-fuera.ps1 -Version E:\<versión> -Clave C:\VigiaFirma\release-key.dpapi -Puente C:\VigiaFirma\Puente.exe -HuellaPuente <SHA-256>`.
   Se niega si `Puente.exe` no es el anotado (rc 4), si hay red (rc 5), si la clave está dentro de la carpeta que vuelve
   al PC de desarrollo, o si la clave tiene permisos de más (rc 3). Si lo firmado no cuadra con la clave de confianza de
   ese Puente, borra las firmas. Deja `firmas-<versión>.txt` con las cuatro huellas, sin nada de la clave.
6. Devuelve solo los cuatro `.sig` y el `.txt`; publica con `release.sh` (tu paso). Compara huellas antes.

### 3. Rotación escalonada sin dejar fuera a nadie — `tools/signing/preparar-relevo.js`

* `node tools/signing/preparar-relevo.js anadir <clave pública nueva>`: pone la nueva en `NextKeys` de Puente **y** de
  Vigía a la vez. Esa versión (la «puente», p. ej. 0.21.0) **se firma con la clave actual**, así que 0.20.17 la acepta.
* `node tools/signing/preparar-relevo.js promover` (en una versión posterior, cuando la mayoría haya actualizado): la
  nueva pasa a ser la única de confianza; esa versión se firma con la nueva.
* `release.sh` ahora corre `preparar-relevo.js comprobar` y **no publica** si Puente y Vigía no confían en las mismas
  claves o si alguna no es una P-256 válida. `--selftest` también lo comprueba (362/0).

Probado con claves de prueba: la clave nueva entra en las dos listas; claves mal pegadas, RSA, la clave actual como
relevo o una clave **privada** pegada por error se rechazan sin tocar nada; con dos relevos no promueve; si las listas
de Puente y Vigía no cuadran, falla; durante el relevo lo firmado con la vieja pasa y, tras promover, ya no.

### Compatibilidad comprobada para la rotación

| Instalación | Versión puente (firmada con la vieja, trae la nueva) | Versión siguiente (firmada con la nueva) |
| --- | --- | --- |
| 0.20.17 | la acepta | solo pasando por la puente (o reinstalando `Vigia-Setup.exe`; datos conservados) |
| La puente instalada | — | la acepta (confía en las dos) |

### Pasos que necesitan tu autorización exacta

1. ~~Ejecutar el paso 1 sobre `~/.vigia` real~~ — **hecho el 30-10 con tu autorización** (ver «Corrección de permisos… aplicada»).
2. Elegir: **rotar** (clave nueva en el equipo de firma + `preparar-relevo.js anadir` en 0.21.0) o **mover** la actual.
3. Decidir sobre las entradas de `CodexSandboxUsers` en `AppData` y `Temp` (afectan a otras herramientas; no se tocan).
4. Reactivar UAC o no (`EnableLUA = 1` necesita reinicio). Sin UAC, el paso 1 solo es una mejora parcial.

Estado: **`SIGNING_BLOCKED`** hasta que la clave con la que se firme 0.21.0 esté fuera de este PC (o rotada allí).

## Corrección de permisos de `.vigia` aplicada (30-10-2026 17:35) — con tu autorización, sin abrir la clave
Autorizada en el `/goal` del 30-10 (§1). Herramienta: `tools/signing/quitar-acceso-sandbox.ps1` (commit `7202134`).
- **Antes** (solo metadatos, `scratch/acl/antes.txt`): `CodexSandboxUsers` con ReadAndExecute explícito en `.vigia`.
- **Copia de los permisos** antes de cambiar nada: `%USERPROFILE%\vigia-acl-antes-20261030-173546.acl` (3106 B; solo tu cuenta, SYSTEM y Administradores pueden abrirla).
- **Aplicado:** rc 0. Herencia cortada en `.vigia`; quedan solo SYSTEM, Administradores y tu cuenta, con control total.
- **Comprobado después** (`scratch/acl/verificar.txt`, repetido a las 18:00): `icacls` de la carpeta y de los tres archivos solo con esas tres cuentas; ninguna entrada de `CodexSandbox*`, `Everyone`, `Authenticated Users`, `Users` ni `INTERACTIVE` (`aces-de-riesgo=0`); ninguna cuenta del sandbox en Administradores ni en Operadores de copia; tamaños, fechas y dueño de los tres archivos **iguales que antes** (la clave: 240 B, modificada 27-10 23:03:06 UTC); tu cuenta crea, lee y borra un archivo de prueba (no la clave) sin dejar rastro; `Puente --key-check` (solo permisos) **rc 0** «Clave protegida».
- **Volver atrás:** `powershell -File tools\signing\quitar-acceso-sandbox.ps1 -Deshacer "%USERPROFILE%\vigia-acl-antes-20261030-173546.acl"`.
- La clave **no se abrió, imprimió, copió, movió ni rotó**. Los permisos de `AppData` y `Temp` no se tocaron.

## Permisos fuera de `.vigia` (30-10-2026, solo metadatos) — corrección limitada propuesta, NO aplicada
| Ruta | Qué hay | Permiso peligroso | Propuesta limitada |
|---|---|---|---|
| `%USERPROFILE%\.puente-signing` | `update-public-key.pem` (113 B) y `update-signing-key.dpapi` (342 B), de 25-10. La pública **no** es la de Vigía (comparada con `PublicKey`): es material de firma de otra herramienta tuya. La privada está protegida con DPAPI de tu cuenta | `CodexSandboxUsers` RX **explícito** (con herencia) | quitar esa entrada |
| `%USERPROFILE%\Puente-respaldos` | copias de perfil de otra herramienta (26-10): almacenamiento local, `Network`, preferencias, transcripciones | `CodexSandboxUsers` RX **explícito** | quitar esa entrada |
| `%LOCALAPPDATA%\Vigia` (tus datos de Vigía) y `%LOCALAPPDATA%\Programs\Vigia` (0.20.17) | ajustes, copias, historial; programa | RX **heredado** de `AppData` | cortar la herencia solo en `%LOCALAPPDATA%\Vigia` y quitar la entrada; `Programs\Vigia` desaparece al migrar |
| `AppData` (RX explícito) y `AppData\Local\Temp` (Modificar explícito + un SID sin nombre `…-3224999056`) | todo tu perfil | amplio | **no tocar**: el sandbox de Codex lo necesita para funcionar. Mitigación: no dejar secretos en `Temp` (las herramientas de firma no lo hacen) |
| Cuentas `CodexSandboxOffline` (último inicio 29-10 13:37) y `CodexSandboxOnline` (nunca) | habilitadas, no administradoras | — | desactivarlas cuando no uses Codex (`OWNER_DECISIONS` §7) |
| `%ProgramData%\Vigia\claves` | clave de las copias | SYSTEM y Administradores, explícito | correcto, nada que hacer |

Comandos (consola de administrador; cada uno guarda copia antes y se deshace con `/restore`):
```
icacls "%USERPROFILE%\.puente-signing" /save "%USERPROFILE%\acl-puente-signing.txt" /t
icacls "%USERPROFILE%\.puente-signing" /remove:g "%COMPUTERNAME%\CodexSandboxUsers" /t
icacls "%USERPROFILE%\Puente-respaldos" /save "%USERPROFILE%\acl-puente-respaldos.txt" /t
icacls "%USERPROFILE%\Puente-respaldos" /remove:g "%COMPUTERNAME%\CodexSandboxUsers" /t
icacls "%LOCALAPPDATA%\Vigia" /save "%USERPROFILE%\acl-vigia-datos.txt" /t
icacls "%LOCALAPPDATA%\Vigia" /inheritance:d
icacls "%LOCALAPPDATA%\Vigia" /remove:g "%COMPUTERNAME%\CodexSandboxUsers" /t
```
Verificar: `icacls <ruta>` sin `CodexSandboxUsers`, y que la herramienta dueña (y Vigía) sigue abriendo.
Volver atrás: `icacls "%USERPROFILE%" /restore "%USERPROFILE%\acl-puente-signing.txt"` (ídem `acl-puente-respaldos.txt`) y `icacls "%LOCALAPPDATA%" /restore "%USERPROFILE%\acl-vigia-datos.txt"` (`/save` guarda rutas relativas a la carpeta padre).

## Firma fuera y relevo de claves — probados sin la clave real (30-10-2026)
- `tools/signing/signing-test.js` con claves de prueba: **39 OK / 0 FAIL** sobre el HEAD final (antes 36; desde `78f3fdf` comprueba también que el relevo sube la versión de Puente). Cubre `preparar-relevo.js`, `quitar-acceso-sandbox.ps1` y `firmar-fuera.ps1` (se niega con red, con Puente cambiado o con la clave con permisos de más).
- `78f3fdf`: `anadir` y `promover` suben la versión de Puente. Motivo: Puente solo se cambia por uno más nuevo; sin la subida, una instalación con Puente de la misma versión se quedaba con la lista de claves vieja y, tras rotar, rechazaba las versiones nuevas (se cortaban las actualizaciones).
- **VigiaLab, fila 38**, con dos claves de laboratorio A y B (la real no se usó): instalación limpia 0.21.0 (solo A) → saltarse la versión puente rechazado sin instalar nada → versión puente (A+B, firmada con A) instalada y Puente 2.0.1 → 2.0.2 → versión solo B (firmada con B) instalada y Puente 2.0.3 → una versión firmada con la vieja A tras rotar, rechazada → rollback y reparación rc 0.
- Para la clave real: la identidad aislada es **un equipo de firma aparte y sin red** (en este PC no hay aislamiento posible con UAC desactivado y tu cuenta administradora). Pasos exactos: `OWNER_DECISIONS_0.21.0.md` §6 y «Procedimiento exacto» de arriba.
- Actualizaciones desde 0.20.17: mientras no se rote, 0.21.0 se firma con la misma clave en la que confía 0.20.17. Si se rota, primero una versión firmada con la actual que ya confía en la nueva; 0.20.17 la acepta porque la firma es la de siempre.

## Revisión 30-10 (noche, 2.ª) — solo metadatos
- `release-key.pem`: 240 bytes, modificada 27-10-2026 19:03 (-04), ACL heredada de `.vigia`: SYSTEM, Administradores y el usuario (sin `CodexSandboxUsers`). No se abrió, copió, movió ni rotó.
- Siguen habilitadas las cuentas locales `CodexSandboxOffline` y `CodexSandboxOnline`; la clave sigue **sin cifrar** en la cuenta de desarrollo con UAC desactivado.
- Procedimiento offline reforzado: el candidato queda atado a su commit y a sus huellas (`candidato.txt`, `release.sh`, `candidate-check.js`); un relevo o una publicación con zip malo ya no cambia Puente ni sus claves de confianza (`6ea6126`). Documentado en `docs/SIGNING.md`.
- **Veredicto: SIGNING_BLOCKED** hasta que decidas la custodia (equipo o cuenta de firma aparte, clave protegida con `--protect-key`) y si se rota por la posible exposición anterior.

## Próximo paso
Tus decisiones de `OWNER_DECISIONS_0.21.0.md` §6 b–d (rotar o mover; permisos fuera de `.vigia`; firmar fuera). Mientras
tanto `release.sh` no firma con la clave de este PC y la release sigue **`SIGNING_BLOCKED`**.

## Revisión 31-10-2026 (madrugada) — solo metadatos, con controles en archivo ficticio
**Acceso de otras cuentas (comprobado ahora, con `icacls` y `Get-LocalGroupMember`, que no abren la clave):**
- `.vigia`: herencia cortada; solo `SYSTEM`, `Administradores` y `Pickle OS`, control total. `release-key.pem` hereda esas tres y nada más. Ninguna entrada de `CodexSandboxUsers`, `Users`, `Everyone` ni `Authenticated Users`.
- `CodexSandboxUsers` = `CodexSandboxOffline` (habilitada, último inicio 29-10 13:37) y `CodexSandboxOnline` (habilitada, nunca). **Ninguna de las dos está en Administradores** (allí solo `Administrator`, deshabilitada, y `Pickle OS`) ni en Operadores de copia. → **Sin acceso a `.vigia`.**
- **Pero** `EnableLUA = 0` y tu cuenta es administradora: cualquier programa que corra como tú (agentes incluidos) lee la clave sin aviso. Las ACL impiden el acceso a otras cuentas, no a lo que corre en la tuya.

**Fecha de último acceso de la clave: `2026-10-30 23:38:49Z`** (escritura 27-10 23:03:06Z, 240 B; `DisableLastAccess = 0`).
Control con un archivo ficticio (`scratch/p10/atime-control.txt`): `Get-Item`, `Get-Acl` e `icacls` **no** mueven esa fecha; leer el contenido, un escaneo de Defender y **`stat`/`ls -l` de git-bash sí** (MSYS abre el archivo y lee sus primeros bytes para decidir si es ejecutable).
Esa fecha coincide al segundo con un `stat -c '%s %y'` que el asistente lanzó desde git-bash el 30-10 a las 23:38:50Z para «leer metadatos»: **fue esa orden**. El contenido no se imprimió ni se guardó (la orden solo devolvía tamaño y fecha), pero es una apertura con lectura hecha por un proceso de tu cuenta. Corregido: sobre la clave solo se usan `icacls` y `Get-Item` de PowerShell. Lo que pasara antes del 30-10 no se puede reconstruir con esa fecha (se sobrescribe).

**Riesgo residual (sin presuponer que las ACL lo borran):**
- La clave estuvo legible para `CodexSandboxUsers` (ReadAndExecute explícito) hasta el 30-10 17:35, y siempre para todo lo que corre como tú con UAC apagado. No hay registro de acceso a archivos (auditoría SACL desactivada), así que **no se puede demostrar que nadie la copió**. Hay que tratarla como **posiblemente expuesta**.
- Recomendación (decisión tuya): **rotar** con relevo escalonado. 0.21.0 se firma una última vez con la clave actual **desde el equipo de firma** y lleva la nueva en `NextKeys`; la siguiente se firma solo con la nueva. Si prefieres no rotar, la custodia mínima es sacarla de este PC y cifrarla con `--protect-key` allí.

**`--protect-key` con claves de laboratorio** (`signing-test.js`, **44 OK / 0 FAIL**, 5 casos nuevos): el `.dpapi` no contiene la clave en claro; queda con herencia cortada y una sola entrada (tu cuenta); no pisa un `.dpapi` existente; un `.dpapi` alterado no descifra ni firma nada; con el bueno firma y la firma pasa con la clave pública de prueba. DPAPI de usuario: otra cuenta u otro equipo no puede descifrarlo; **un proceso de tu misma cuenta sí**, así que la protección real es el equipo aparte.

**Entorno de firma separado — pasos exactos (manual, sin secretos aquí):** ver «Procedimiento exacto» §2–§3 arriba y `docs/SIGNING.md` («Dedicated signing environment»). Resumen para el día que lo apruebes:
1. Equipo de firma sin red, sin agentes ni herramientas de desarrollo. Compila allí `Puente.exe` desde el commit revisado (o cópialo y anota su SHA-256 en papel).
2. Rotar (recomendado): allí `Puente.exe --genkey C:\VigiaFirma\nueva.pem`, luego `--protect-key nueva.pem nueva.dpapi`, borra el `.pem`. Trae **solo la clave pública** y aquí `node tools/signing/preparar-relevo.js anadir <pública>` (sube Puente a 2.0.2), commit, nuevo candidato.
3. Mover la actual (si así lo decides): USB → equipo de firma → `--protect-key` → borrar el `.pem` del USB y de `~/.vigia`. **Mover o borrar la clave real necesita tu autorización explícita.**
4. Aquí: `VIGIA_RELEASE_SIGN=fuera bash tools/release.sh "<notas>"` → `out\0.21.0` con `candidato.txt`. Allí: `firmar-fuera.ps1 -Version E:\0.21.0 -Clave C:\VigiaFirma\release-key.dpapi -Puente C:\VigiaFirma\Puente.exe -HuellaPuente <SHA-256>`. Vuelven solo los `.sig` y `firmas-0.21.0.txt`. Aquí: `node.exe tools/candidate-check.js out/0.21.0` y `VIGIA_RELEASE_SIGNED=1` (se niega si lo firmado no es lo compilado).

**Ruta de actualización de 0.20.17:** comprobada en VigiaLab (fila 41, `6f8e5ff`, claves de laboratorio): 0.20.17 con Puente 1.4.0 y la clave A baja y verifica el Puente de transición 2.0.2 (A+B), que reemplaza al antiguo comprobado por versión **y** SHA-256 (`03854adb…`); un Puente alterado o firmado con otra clave no se baja; relevo A→B; Puente 2.0.1→2.0.2→2.0.3; zip alterado rc 1 sin tocar Puente; downgrade con firma válida rechazado; rollback rc 0; 135/135 archivos de datos iguales.

**Veredicto: `SIGNING_BLOCKED`** — `NEEDS_OWNER_APPROVAL`: custodia (equipo de firma), rotar o mover, y si desactivar `CodexSandboxOffline`/`Online` y reactivar UAC.

## Firma fuera y relevo A → B preparados (31-10-2026) — sin tocar la clave real

Sustituye a los pasos de «Entorno de firma separado — pasos exactos» de arriba (que creaban B con `Puente --genkey` en
claro). El procedimiento vigente está en **`docs/FIRMA_OFFLINE.md`**.

**Clave A — permisos comprobados de nuevo** (`scratch/p11/acl-a.ps1`: `icacls` y `Get-Item`, sin abrir el archivo):
- `.vigia`: `SYSTEM`, `BUILTIN\Administrators`, `Pickle OS` (control total, `(OI)(CI)`); `release-key.pem` hereda esas tres
  `(I)(F)` y nada más. Sin `CodexSandboxUsers`, `Users` ni `Everyone`.
- 240 B; creada y escrita 27-10 23:03:06Z; último acceso **30-10 23:38:49.5591085Z**, el mismo antes y después de la consulta.
- `CodexSandboxUsers` = `CodexSandboxOffline` (habilitada, último inicio 29-10 13:37:34) y `CodexSandboxOnline`
  (habilitada, nunca). Administradores: `Administrator` y `Pickle OS`.
- Riesgo histórico: sin cambios. Las ACL corregidas no dicen nada de lo que pasó antes del 30-10 17:35, y con
  `EnableLUA = 0` todo lo que corre con tu cuenta puede leer la clave. **A se trata como posiblemente comprometida.**

**Clave B:** no existe. Se crea en el equipo de firma con `VigiaFirma nueva` (nunca en claro en disco: `.dpapi` de esa
cuenta + respaldo PKCS#8 cifrado con frase, AES-256-CBC, PBKDF2-SHA256 600 000). Al PC de desarrollo solo vuelve
`vigia-B.publica.txt`. `tools/signing/sin-clave-b.ps1` en este PC: **rc 0, ninguna clave de VigiaFirma** en el perfil ni
en `C:\ProgramData`.

**Pública A registrada:** `tools/signing/claves-publicas/vigia-A.publica.txt`, huella
`ab29dbf9ab37ae1848561d63b1811c0c764c2e37282e69aa82810b147136bc08`, la misma que llevan `app/Puente/Program.cs`,
`app/Vigia/Updates.cs` y `v0.20.17`.

### Herramientas del equipo de firma

| Archivo | SHA-256 |
|---|---|
| `out/vigiafirma/VigiaFirma.exe` (11 641 265 B, de `tools/signing/estacion`) | `be9ab4d75483a74c96e7f6cff81ceb02c90281796432790355a641c6a669616f` |
| `tools/signing/firmar-fuera.ps1` | `498c232c22168af4baffe99347230fce053225356f20d37f90a32cc1a55ea56c` |
| `tools/signing/claves-publicas/vigia-A.publica.txt` | `1c9c9aea207cde2c47d45c542093b08edd9ebee39f35b4b9b0d0367ce1366b27` |

`VigiaFirma.exe` no va en Vigía ni en Puente; no usa la red. Si se recompila, su huella cambia (la compilación de un
solo archivo no es determinista): copia al equipo de firma **este** archivo y compara con esta tabla.

### Qué protege cada comprobación (probado con claves de laboratorio)

- `estacion-test.js`: **49 OK / 0 FAIL**, con el Puente de prueba recompilado del código actual (el anterior era de
  antes del último cambio de `app/Puente`). Crea B sin nada en claro; el respaldo está cifrado y la salida no muestra ni
  clave ni frase; `importar` no toca el `.pem`; `verificar` (código distinto del de Puente) rechaza otra clave, un
  manifiesto cambiado, una pieza distinta de la hoja de huellas y firmas intercambiadas; `firmar-fuera.ps1` no firma con
  red (rc 5), con la hoja dentro de la carpeta o piezas distintas (rc 4), quita las firmas si no verifican con la pública
  exigida (rc 6) y, en la transición, no firma si el Puente de la versión no confía en B (rc 7).
- `signing-test.js`: **48 OK / 0 FAIL** (antes 44; se adaptó a la interfaz nueva de `firmar-fuera.ps1` y se añadieron:
  hoja de huellas dentro de la carpeta, manifiesto sustituido, firmado con A pero exigida otra pública).
- `release-test.js`: **35 OK / 0 FAIL** (antes 33): `VIGIA_RELEASE_SIGNED=1 VIGIA_RELEASE_DRY=1` comprueba lo
  firmado fuera y para sin tocar la red; un manifiesto cambiado después de firmar no pasa.
- `candidate-check.js`: las claves de relevo tienen que estar registradas en `tools/signing/claves-publicas/`, ser
  iguales en Puente y en Vigía y estar en el `Vigia.dll` compilado.

### Riesgo que el relevo no quita

Quien tenga A puede firmar una actualización falsa que aceptan 0.20.x (Puente 1.4.0) y la transición (Puente 2.0.2,
A + B) hasta que instalen la versión que retira A (Puente 2.0.3). Esa actualización podría instalar un Puente que confíe
en otra clave. La cadena A → B acorta la ventana, **no la elimina**. Mitigación: publicar la retirada de A poco después
de la transición, y la recuperación manual por canal verificado aparte (`docs/FIRMA_OFFLINE.md`, «Recuperación»).

**Veredicto: `SIGNING_BLOCKED`.** Falta el equipo de firma (lo preparas tú) y los pasos con claves reales:
crear B, pasar A, firmar. Están marcados ⏸ en `docs/FIRMA_OFFLINE.md`.
