# Vigía — informe de la migración de seguridad (0.21.0 / Puente 2.0)

Rama `debloat-v2` (base `efac823`), 2026-10-29. Sin push, sin merge, sin release. **La instalación del PC de desarrollo no
se tocó** (sigue la 0.20.17 en `%LOCALAPPDATA%\Programs` con su tarea elevada).

Leyenda: **[probado]** = lo comprueba una prueba automática que pasó; **[código]** = implementado y revisado, sin prueba
propia; **[VM]** = falta validarlo en una VM con UAC activado (en este PC UAC está apagado, así que nunca sale el aviso).

Detalle técnico: `docs/PRIVILEGES.md` (permisos por operación), `docs/INSTALL.md` (instalación, migración, recuperación,
validación en VM), `docs/SIGNING.md` (firma y clave), `DEBLOAT_IMPLEMENTATION_REPORT.md` (Apps).

## 1. Problemas de seguridad confirmados (contrastados con el código)

| # | Problema | Dónde estaba | Estado |
|---|---|---|---|
| 1 | Vigía pedía administrador siempre (`requireAdministrator`) | `app/Vigia/app.manifest` | Cerrado: `asInvoker` [código] |
| 2 | Tarea de inicio `\Vigia\Iniciar Vigia` con «privilegios más altos» lanzando un `.exe` de una carpeta escribible por el usuario (escalada silenciosa) | `AutoStart.cs` | Cerrado: inicio por `Run` de HKCU, sin permisos [probado]; Puente quita la tarea vieja al migrar [probado] |
| 3 | Vigía y Puente en `%LOCALAPPDATA%\Programs` (cualquier proceso del usuario podía cambiar los binarios que luego corrían como admin) | Puente 1.x | Cerrado: `%ProgramFiles%\Vigia` con ACL propia [probado] |
| 4 | Puente aceptaba una versión más vieja bien firmada (downgrade) | `UpdateSelf`, `EnsurePuenteAsync` | Cerrado: `estado.json` con `highest` y `floor`; Puente nunca se baja [probado] |
| 5 | Comprobación y uso separados (TOCTOU): descarga y comprobación en `%TEMP%`, ejecución después | Puente 1.x, `Updates.cs` | Cerrado: descarga y extracción en carpeta solo-admin; en Vigía la firma se comprueba sobre los bytes en memoria y a disco van esos bytes [probado] |
| 6 | Copias en `%LOCALAPPDATA%` editables y restauradas como administrador (se podía escribir cualquier clave o archivo) | `Backups.cs` | Cerrado: `BackupGuard` (esquema + destinos cerrados + sello HMAC) [probado] |
| 7 | `release.sh` creaba la release publicada antes de subir los archivos | `tools/release.sh` | Cerrado: borrador → subida → verificación → publicar [probado con mock] |
| 8 | Variable de prueba `PUENTE_TEST_CUT` activa en el Puente publicado | `app/Puente/Program.cs` | Cerrado: solo en el build `PUENTE_TEST` [probado: «la Puente publicada no tiene los comandos de prueba»] |
| 9 | Clave privada de firma en texto plano y legible por el grupo local `CodexSandboxUsers` | `~/.vigia/release-key.pem` | **Abierto: necesita tu autorización** (sección 11) |
| 10 | `openUrl` abría cualquier ruta de `github.com` | `Support.cs` | Cerrado: solo HTTPS, sin usuario ni puerto, en GitHub solo Vigía [probado] |
| 11 | Lecturas que sin permisos devolvían un resultado falso («todo por defecto», «0 bytes») | `Diagnostics.cs`, `Security.cs`, `Health.cs`, `Cleanup.cs` | Cerrado: dicen «no se pudo leer / sin permisos» o van al ayudante [probado en parte] |
| 12 | Un argumento mal escrito en Puente podía acabar en instalar con permisos | `app/Puente/Program.cs` | Cerrado: `BadArgs`, código 2 [probado] |
| 13 | Cinco validaciones de entrada (sc.exe, planes de energía, arranque de servicios, `procClose`, descargas en la ventana/CSP) | commit `2d05753` | Cerrado [probado] |

Descartados tras revisar el código (ya estaban bien): PawnIO sin consentimiento, origen de mensajes de WebView2
(`UiMessage.Accept`), atajos/iframes, rutas de herramientas desde la UI, ZIP Slip (se rechaza `..`).

## 2. Nueva arquitectura de privilegios

- `Vigia.exe` corre **como tu usuario** (`asInvoker`): ventana, WebView2, lecturas y diagnósticos.
- Lo que necesita administrador lo hace un **ayudante bajo demanda**: el mismo `Vigia.exe --broker`, abierto con UAC la
  primera vez que hace falta y cerrado con la ventana. Elegido en vez de un servicio porque un servicio con permisos estaría
  siempre escuchando aunque no uses Vigía, y Vigía no necesita nada con permisos en segundo plano.
- Canal: tubería con nombre aleatorio creada por la ventana antes de elevar (`FirstPipeInstance`), ACL solo para tu cuenta,
  sin red, comprobación mutua de PID y de que el ayudante es el `Vigia.exe` de la carpeta protegida.
- Lista cerrada `AdminOps.Names` (41 operaciones); cada parámetro se vuelve a validar **dentro** del ayudante. Ninguna acepta
  rutas de programas, claves de registro ni texto de PowerShell/CMD. Lo que no está en la lista se rechaza [probado].
- El ayudante no arranca si Vigía no está en la carpeta protegida (una 0.21 todavía en la carpeta vieja funciona, pero sin
  ayudante hasta migrar) [código].
- Clasificación completa de las 89 operaciones de la ventana (41 con administrador, 48 sin): `docs/PRIVILEGES.md`.
  Las «suaves» (lecturas como inventario de Apps, salud, diagnóstico, seguridad, limpieza) no abren UAC por su cuenta: si el
  ayudante no está abierto, se hace la versión sin permisos y se dice qué no se pudo leer.

## 3. Ubicación de instalación

`%ProgramFiles%\Vigia` (de `Environment.SpecialFolder`, no escrita a mano: no depende del idioma ni de la unidad).
Contenido: `Puente.exe`, `versiones\<x.y.z>\` (cada versión aparte con su lista de huellas `.vigia-archivos.json`),
`estado.json`, `diario.json`, `.descargas\`. Datos tuyos en `%LOCALAPPDATA%\Vigia` (ajustes, historial, copias, logs, estado
de la ventana) y exportaciones en `Documentos\Vigía`. Compartido con permisos: `%ProgramData%\Vigia\registros` y
`%ProgramData%\Vigia\claves\copias.key` (solo SYSTEM y Administradores). Tabla completa: `docs/INSTALL.md`.

## 4. Permisos NTFS

Herencia cortada; SYSTEM y Administradores control total; Users y ALL APPLICATION PACKAGES (WebView2) solo leer y
ejecutar; dueño Administradores (`UseAdminsAsOwner`); nada oculto ni de solo lectura. Si la raíz, `versiones`, una versión o
un archivo es un junction/enlace, Puente no instala. `Acl.Problem` revisa la carpeta entera antes de dar una versión por
buena. **[probado]** con un usuario sin permisos simulado (token SAFER): no puede cambiar, renombrar, borrar ni poner una DLL
al lado, ni dejar nada en la raíz ni en `.descargas`. **[VM]** con una cuenta estándar real.

## 5. Inicio automático

Entrada `Vigia` en `HKCU\...\Run` → `"<versión activa>\Vigia.exe" --tray`. Sin UAC, sin `HighestAvailable`, sin tarea.
Instancia única (mutex), arranca en bandeja, espera si hay un juego en pantalla completa. Se activa/desactiva desde Ajustes
(como tu usuario). Al migrar, Puente lee el XML de `\Vigia\Iniciar Vigia` y **solo la borra si su ejecutable es un
`Vigia.exe` de Vigía**; si estaba activa, crea la entrada `Run` en su lugar. Una tarea con ese nombre de otro programa y las
entradas `Run` de otros programas no se tocan **[probado]**. El inicio nunca abre el ayudante **[probado]**.

## 6. Procedimiento de migración

Las 0.20.x se actualizan bajando el `Puente.exe` de la última release (firma comprobada con la misma clave pública) y
abriéndolo con `--update --wait <pid>`: ese Puente es el 2.0, así que **la actualización normal hace de instalador de
transición**. Puente 2.0: detecta la instalación vieja y su versión → no ejecuta ni copia nada de ella → instala desde el
paquete firmado en Program Files → ACL → `--install-check` de la nueva como tu usuario → activa en una sola escritura →
accesos nuevos → inicio migrado → **solo entonces** borra las carpetas viejas (si alguna está en uso, se reintenta en el
siguiente arranque). Tus datos no se mueven. Pasos y comprobaciones: `docs/INSTALL.md`.
**[probado]**: «detecta la 0.20.17 vieja y migra», «ajustes, copias e historial quedan intactos», tarea y Run, accesos,
junction en la carpeta vieja. **[VM]**: la cadena real con el Vigía 0.20.17 y el Puente viejo publicados.

## 7. Cambios en Puente (2.0)

Abre sin permisos y pide UAC solo para instalar/cambiar (se reabre a sí mismo con el `.exe` retenido para que no lo cambien
mientras sube). Comandos: instalar, `--offline <carpeta>`, `--repair`, `--rollback`, `--uninstall`, utilidades de firma
(`--sign`, `--verify`, `--manifest`, `--verify-manifest`, `--protect-key`) y autopruebas (`--zip-test`, `--acl-test`).
Diario de recuperación, espacio libre, reanudación de descargas, nunca sobrescribe una versión abierta (sin problema de DLL
bloqueadas), recupera tras cortes. Los comandos de prueba solo existen en el build `Puente-test`.

## 8. Protección contra downgrade

`estado.json` (carpeta protegida) guarda la versión activa, la anterior, la más nueva vista (`highest`) y el piso de seguridad
(`floor`, del campo `minVersion` del manifiesto firmado; `MakeManifest` rechaza un `minVersion` mayor que la versión).
Un manifiesto viejo bien firmado no hace volver atrás; `--rollback` solo vuelve a la anterior si sus archivos están intactos y
no está por debajo del piso; Puente nunca se reemplaza por uno más viejo (ni desde Puente ni desde Vigía). **[probado]**

## 9. Cambios en `release.sh`

Compilar → pruebas (selftest de Vigía aislado, `--zip-test`, `--acl-test`, traducciones) → paquete → manifiesto con hashes →
firmar → verificar firmas y hashes con el Puente recién compilado → release **borrador** → subir → comprobar desde GitHub
nombres, tamaños y manifiesto byte a byte → etiqueta → publicar. Si algo falla queda en borrador; reintentar retoma el mismo
borrador; una versión publicada nunca se toca; no cambia «Latest» de otras releases. Contra GitHub real los repos de destino
son fijos. **[probado]** contra un GitHub falso local (`tools/release-test.js`, 25 comprobaciones, solo 127.0.0.1, clave de
prueba). No se hizo ninguna publicación real.

## 10. Protección de copias

`BackupGuard` trata cada copia de `%LOCALAPPDATA%\Vigia\copias` como dato no confiable: (1) esquema y tipos de operación;
(2) lista cerrada de destinos — claves de registro bajo raíces conocidas, servicios que existen con modos normales, GUID de
planes de energía válidos, archivos solo si son el `.ini` del juego o están bajo la ruta de inicio, sin `..`, junctions ni
enlaces; (3) sello HMAC-SHA256 que pone el ayudante, con la clave en `%ProgramData%\Vigia\claves\copias.key` (ni lectura para
usuarios). El sello no basta solo: el ayudante valida contenido y destino siempre. Una copia sin sello (las viejas) se puede
restaurar si pasa (1) y (2), pero no puede volver a apagar Defender ni SmartScreen: como mucho los deja como de fábrica.
Lo que no se puede restaurar de forma segura da un error explicado. **[probado]** (selftest, sección de copias).

## 11. Estado del proceso de firma

- La clave **no está en git**: ningún `.pem/.key/.dpapi` ni bloque `PRIVATE KEY` en ningún commit (revisado).
- `release.sh` no la copia, pasa solo la ruta, rechaza una clave dentro del repo, y el token de GitHub no se imprime.
- `Puente --protect-key` cifra la clave con DPAPI (tu cuenta + entropía propia, ACL solo tuya); probado con una clave de prueba.
- **Hallazgo nuevo**: `%USERPROFILE%\.vigia` tiene una entrada explícita `CodexSandboxUsers:(OI)(CI)(RX)`; los miembros son
  las cuentas de sandbox de Codex (`CodexSandboxOffline`, `CodexSandboxOnline`). La segunda tiene red: un proceso en esa sandbox
  puede leer la clave y sacarla. **No lo cambié** (toca la clave y quizá cómo trabaja Codex). Procedimiento en `docs/SIGNING.md`.
- Rotación de clave: procedimiento documentado en `docs/SIGNING.md`, no ejecutado.

## 12. Compatibilidad con versiones anteriores

Las 0.20.x publicadas actualizan sin descargar nada aparte (sección 6). El formato nuevo (manifiesto firmado) no lo entiende el
Puente viejo, pero el Puente viejo solo hace falta para bajar el Puente 2.0, cuya firma sí comprueba. Una versión sin manifiesto
firmado (formato viejo) no la instala Puente 2.0 [probado]. **[VM]** la actualización real desde 0.20.17.

## 13. Archivos modificados (desde `efac823`)

56 archivos, +7421 / −595 (incluye Debloat V2).
- **Nuevos**: `app/Vigia/{Elevation,AdminOps,BackupGuard,AppInventory,AppCatalog,AppPlan,AppHistory}.cs`, `app/Vigia/apps.json`,
  `app/Puente/{Native,Layout,Setup,TestHooks}.cs`, `tools/{puente-test,release-mock,release-test,ui-apps-test,i18n-add-debloat}.js`,
  `tools/puente-test/fake-vigia/*`, `docs/{PRIVILEGES,INSTALL,SIGNING}.md`, `.gitattributes`, `DEBLOAT_IMPLEMENTATION_REPORT.md`,
  este informe e `IMPLEMENTATION_PROGRESS.md`.
- **Cambiados**: `app/Vigia/{AutoStart,Backups,Bios,Cleanup,Debloat,Diagnostics,Games,Procs,Program,Security,Settings,Startup,Support,Tweaks,Updates}.cs`,
  `Vigia.csproj`, ambos `app.manifest`, `app/Puente/{Program.cs,Puente.csproj}`, `wwwroot/{index.html,app.js,app.css,changelog.json}`,
  `wwwroot/i18n/*.json`, `tools/{release.sh,install.ps1,i18n-extract.js}`.

## 14. Commits locales (`debloat-v2`)

`b3e554b` `7e8de38` Apps · `2d05753` validaciones · `3a59be2` Vigía como tu usuario + ayudante · `33870a1` Puente 2.0 ·
`d3b7f5a` prueba de `release.sh` · `a1d6c71` bash con LF · `52fc733` lecturas admin vía ayudante · `1dcce45` repos fijos ·
`f3c17e3` novedades en 5 idiomas · `b4f9424` docs · `23b88b6` enlaces · `84cb4b9` firma de Puente en memoria ·
`82ca9c3` limpieza sin permisos · más el commit de este informe.

## 15. Pruebas ejecutadas (última corrida)

| Prueba | Resultado | Baseline | Diferencia |
|---|---|---|---|
| `Vigia.exe --selftest` (carpeta aislada) | **300 OK / 0 FAIL** | 230 | +70 nuevas: permisos, ayudante, copias, enlaces, firma de Puente, limpieza sin permisos |
| UI de Apps (`tools/ui-apps-test.js`, Chrome headless, WebView simulado) | **44 / 0** | 44 | igual |
| Traducciones (`tools/i18n-check.js`) | **0 errores** (2868 claves × en/pt/fr/de) | 0 | igual |
| Puente (`tools/puente-test.js`, `%TEMP%`, usuario simulado con SAFER) | **57 / 0** | — | nueva |
| `release.sh` contra GitHub falso (`tools/release-test.js`) | **25 / 0** | — | nueva |
| Build Release Vigía y Puente | 0 errores | — | — |

No probado de verdad (solo simulado o nada): UAC real, cuenta estándar real, la ventana real de Vigía con el ayudante,
desinstalaciones de Apps, migración real, cortes de luz reales, antivirus bloqueando archivos.

## 16. Riesgos pendientes

1. Clave de firma legible por `CodexSandboxUsers` y sin cifrar (sección 11).
2. Todo lo marcado **[VM]**: el aviso de UAC, una sola vez por sesión, y el ayudante con otra cuenta (si UAC se aprueba con
   otra cuenta, el ayudante no arranca: previsto, pero hay que ver que el mensaje se entiende).
3. `tools/install.ps1 -Dev` publica en `%LOCALAPPDATA%\Programs\Vigia` (la carpeta de tu instalación actual): solo para un PC de pruebas. El atajo `VIGIA_DEV` se quitó; ninguna variable abre el ayudante desde una carpeta sin proteger [probado].
4. La carpeta `\Vigia\` del Programador de tareas puede quedar vacía tras borrar la tarea vieja (inofensivo).
5. Restos `vigia-selftest-*` de una corrida de hoy a las 02:44 en `%TEMP%` (las corridas actuales limpian); no los borré.
6. (Resuelto) las capturas de QA ya no abren ventana en tu escritorio.

## 17. Operaciones que requieren tu autorización

- Quitar `CodexSandboxUsers` de `%USERPROFILE%\.vigia` y/o cifrar la clave con DPAPI (y guardar copia offline del `.pem`).
- Migrar tu instalación real (0.20.17 → 0.21.0).
- Push, merge a `main`, publicar 0.21.0 (o una release de prueba en un repo de pruebas).
- Rotar la clave de firma.
- Borrar los restos de `%TEMP%`.
- Desinstalaciones reales de Apps (solo en VM).

## 18. Validar la migración en VM

Pasos completos en `docs/INSTALL.md` → «Validar en una VM». Resumen: VM Win11 (y Win10) con **UAC activado**, cuenta admin y
cuenta estándar, snapshot; instalar 0.20.17 con inicio activo y datos → actualizar a 0.21.0 (repo de pruebas o `--offline`) →
comprobar tarea vacía, `Run` nuevo, carpetas viejas fuera, datos intactos, `icacls`/dueño/sin enlaces, arranque sin UAC →
cuenta estándar no puede escribir en Program Files → ayudante con un UAC por sesión, copia manipulada rechazada → apagar en
seco en «preparando» y «activando» → `--repair`, `--rollback` → manifiesto viejo rechazado → desinstalar → repetir sin red.

## 19. Recuperación si la instalación falla

Tabla completa en `docs/INSTALL.md` → «Recuperación si algo falla». En corto: lo activo no cambia hasta la última escritura
de `estado.json`; un corte se resuelve al abrir Puente con `diario.json`; `Puente.exe --repair` reinstala desde el paquete
firmado; `Puente.exe --rollback` vuelve a la anterior si está intacta y no es insegura; la instalación vieja no se borra hasta
que la nueva está activa. Si Puente mismo no abre: como administrador, borrar `%ProgramFiles%\Vigia` y volver a ejecutar el
instalador. Ninguna recuperación toca `%LOCALAPPDATA%\Vigia` ni `Documentos\Vigía`.
