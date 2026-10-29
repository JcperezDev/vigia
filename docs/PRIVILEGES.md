# Permisos de Vigía

Vigía (`Vigia.exe`) se abre como tu usuario (`asInvoker`). La ventana, WebView2, las lecturas y los ajustes de tu usuario
no piden UAC. Lo que de verdad necesita administrador lo hace un **ayudante bajo demanda**: el mismo `Vigia.exe`
instalado, abierto con UAC solo en ese momento (`Vigia.exe --broker`). Cuando cierras Vigía, el ayudante se cierra.

Código: `app/Vigia/Elevation.cs` (ayudante y canal), `app/Vigia/AdminOps.cs` (lista cerrada y validación).

## Por qué un ayudante y no un servicio

Un servicio de Windows con permisos estaría siempre encendido y escuchando, aunque no uses Vigía. El ayudante solo existe
mientras hace falta y solo habla con la ventana que lo abrió. Vigía no necesita nada en segundo plano con permisos
(el inicio con Windows y el monitor van como tu usuario), así que el servicio sumaría superficie de ataque sin aportar nada.

## Garantías del canal

- Tubería con nombre aleatorio creada por la ventana **antes** de abrir el ayudante (`FirstPipeInstance`): nadie puede
  haberla creado antes.
- ACL de la tubería: solo tu usuario (lectura y escritura) y SYSTEM; acceso por red **denegado**.
- Cada lado comprueba al otro: la ventana, que el cliente es el PID que ella abrió y el mismo `Vigia.exe`; el ayudante,
  que el dueño de la tubería es el PID recibido, el mismo `Vigia.exe` y la misma cuenta. Si en UAC se elige otra cuenta
  de administrador, el ayudante no arranca.
- El ayudante **no arranca** si su carpeta no está protegida (`InstallTrust`: dueño Administradores/SYSTEM, sin permisos de
  escritura para Users, Authenticated Users, Everyone, INTERACTIVE ni paquetes de apps, y sin enlaces). Desde
  `%LOCALAPPDATA%\Programs` no funciona: primero hay que migrar con Puente.
- Mensajes: solo nombres de `AdminOps.Names`. Cada parámetro se valida **otra vez dentro del ayudante**: ids con forma fija
  (`IdRx`, `CleanRx`), letras de unidad, rutas `.iso` locales fuera de carpetas del sistema (`PathGuard`), números en rango.
  Ningún mensaje lleva rutas de programas, claves de registro, texto de PowerShell, CMD ni líneas de órdenes.
- `VIGIA_DATA` y `VIGIA_FORTNITE_INI` se ignoran dentro del ayudante. No hay variable ni opción que abra el ayudante desde una carpeta sin proteger (selftest: «VIGIA_DEV=1 no abre el ayudante…»).

## Clasificación de operaciones

### Con administrador (pasan por el ayudante) — 41

| Área | Operaciones | Por qué necesita administrador |
|---|---|---|
| Ajustes | `tweaksApply`, `tweaksRevert`, `undoRetired`, `sugFix` | Registro de la máquina (HKLM), `powercfg`, `bcdedit`, servicios |
| Servicios / inicio | `startupSet`, `startupRemove`, `startupOffAll`, `autoStartRepair` | Entradas de HKLM, servicios, tarea vieja elevada |
| Apps (Debloat) | `appsScan`, `appsPreview`, `appsRun`, `appsCancel`, `appsRestore`, `bloatRemove`, `simpleRun` | Inventario de todos los usuarios y aprovisionadas; quitar aprovisionamiento |
| Seguridad | `secEnable`, `secScan`, `secGet` | Defender, SmartScreen y TPM solo se leen/cambian con administrador |
| Discos | `optRun`, `optAnalyze`, `optCancel` | `defrag` / TRIM |
| Limpieza | `cleanRun`, `cleanScan` | Carpetas del sistema (caché de Windows Update, temporales de Windows): tu usuario no puede ni listarlas |
| Memoria | `memFree`, `memAuto` | Vaciar la lista de espera (`SeProfileSingleProcessPrivilege`) |
| Procesos | `procClose` | Cerrar procesos de otros usuarios o elevados (solo los de la última lista mostrada) |
| Copias | `backupRestore`, `restorePoint` | Restaurar HKLM/servicios; punto de restauración del sistema |
| Diagnóstico | `healthScan`, `diagRun` | Opciones de arranque (`bcdedit`) y SMART de discos |
| ISO / USB | `isoInspect`, `isoBuild`, `isoCancel`, `usbList`, `usbWrite` | Montar la ISO, DISM, escribir en un USB |
| Monitor / FPS | `fpsStart`, `fpsCancel`, `telemetry` | Sesión ETW del kernel (DxgKrnl); sensores de la placa |
| BIOS | `biosReboot` | Reiniciar directo a la BIOS (`shutdown /fw`). **Vigía nunca escribe en la BIOS** |
| Driver | `pawnInstall` | Instalar PawnIO (solo si respondes «Sí» en el aviso del Monitor) |
| Control | `ping` | Comprobar que el ayudante responde |

Las «suaves» (`appsScan`, `appsPreview`, `telemetry`, `memAuto`, `undoRetired`, `healthScan`, `diagRun`, `secGet`, `cleanScan` y los
`*Cancel`) no abren el aviso de UAC por su cuenta: si el ayudante no está abierto, se hace la versión sin permisos
o se espera a que lo abra una acción tuya.

Sin permisos, las lecturas no inventan resultados: «Reloj del sistema» y «TPM» dicen *«Solo se puede leer con permisos
de administrador; esta vez no se pudo revisar»* en lugar de «todo por defecto» o «no hay TPM». En Limpieza, una carpeta
que tu cuenta no puede listar sale como *«Sin permisos para mirar»* (se puede elegir igual; limpiar la hace el ayudante), no
como «0 bytes».

### Sin administrador (en la ventana, como tu usuario) — 48

| Área | Operaciones |
|---|---|
| Ajustes de Vigía | `settingsGet`, `settingsSet`, `version`, `supportGet`, `openSource`, `openUrl` (solo HTTPS a la lista de sitios; en GitHub solo `github.com/JcperezDev/…`) |
| Lecturas | `suggest`, `tweaksList`, `bloatList`, `startupList`, `simplePlan`, `backupsList`, `optStatus`, `rates`, `drift`, `gamesGet`, `biosGet`, `biosSupport`, `displayStatus`, `toolsList`, `procWatch` |
| Tu usuario | `autoStartSet` (Run de HKCU), `wallGet`, `wallSet`, `displayStretch`, `displayKeep`, `displayRestore` (pantalla con `CDS_UPDATEREGISTRY`, sin `CDS_GLOBAL`), `fortniteApply` (.ini en tu perfil), `rigKeep`, `fpsDelete`, `appsExport`, `appsSettings` |
| Diálogos | `isoPick`, `isoSaveAs`, `usbPickIso`, `isoShow`, `procReveal`, `secOpen`, `toolOpen` |
| Descargas | `isoGetWindows`, `isoDlCancel` (ISO oficial de Microsoft a tu carpeta), `usbCancel` |
| Herramientas | `toolInstall` (WinGet con el paquete verificado; WinGet pide UAC él mismo si el instalador lo necesita) |
| Actualizar | `updateCheck`, `updateRun` (abre Puente, que pide UAC para instalar) |
| Reinicio | `rebootNow`, `rebootCancel` (`shutdown /r`, permitido a la sesión interactiva) |
| PawnIO | `pawnAnswer` (solo guarda tu respuesta; la instalación es `pawnInstall`) |

`autoStartSet` y `displayStretch` salieron de la lista del ayudante a propósito: escriben en tu usuario (HKCU / tu pantalla),
y hacerlo desde el ayudante las mandaría a la cuenta equivocada si UAC se aprueba con otra cuenta.

## Pruebas

`Vigia.exe --selftest` (secciones «permisos (carpeta protegida)» y «permisos (operaciones de administración)») comprueba:
comando fuera de la lista rechazado, parámetros con rutas/`..`/caracteres de control rechazados, ISO solo local y `.iso`,
cada fallo devuelve a la pantalla su «terminado», y el inicio con Windows nunca abre el ayudante.

**Pendiente de validar en VM con UAC activado** (en el PC de desarrollo UAC está desactivado, así que el aviso no aparece):
que cada operación de la tabla de administrador muestre UAC una sola vez por sesión y funcione, y que las de la tabla sin
administrador funcionen con una cuenta estándar.
