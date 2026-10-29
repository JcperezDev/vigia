# Sensores CPU: integración de dependencia y límites

2026-10-29 · debloat-v2 · cambio local, no publicado.

## IMPLEMENTED
- Puente llama `--setup-sensors` en Activate, después de comprobar archivos instalados. Se aplica también al camino Reparar y actualización. No se ejecuta en fixtures PUENTE_TEST.
- Entrada de Vigía exige administrador y carpeta confiable (`InstallTrust.Problem`). Rechaza juego detectado con código 4, no cambia el driver. No sirve para ejecutar comandos arbitrarios.
- Se usa el instalador oficial existente, con hash fijado y bloqueo del archivo mientras se ejecuta. No se renombra ni modifica el driver firmado.
- Monitor deja de programar PawnAsync y no emite pawnAsk. Mensajes antiguos pawnAnswer ya no instalan. La configuración PawnConsent heredada no decide instalación.
- Éxito del broker reactiva CPU. Dump incluye sensores de la instancia CPU tardía.
- Instalador ausente y juego detectado producen error explícito en Pawn. Código de salida no cero deja de acreditarse como éxito; un intento fallido no adquiere propiedad de un driver ajeno.
- Puente registra resultado/errores/aplazamiento. Presenta la fase como sensores de Vigía y mantiene atribución PawnIO.

## Hallazgo (probado en este PC)
- Con token de usuario básico (sin Administradores, sin ventana), `Vigia --probe` da **temp: None** en el Ryzen 9 9900X, aunque PawnIO esté instalado. Antes solo avisaba «pueden no estar disponibles». Con UAC normal ese es el caso de casi todos: Vigía corre `asInvoker`.

## IMPLEMENTED (segunda tanda)
- `app/Vigia/SensorHost.cs`: servicio de solo lectura «Sensores de Vigía» (`Vigia.exe --sensor-service`): solo como SYSTEM y desde carpeta confiable (si no, código 3). Lee la CPU solo mientras hay una ventana de Vigía abierta. Publica grados+hora en memoria compartida `Global\VigiaSensores` (escribe SYSTEM; leen Administradores y usuarios autenticados). No hay tubería, puerto ni archivo de entrada: no recibe órdenes.
- Lector en `Telemetry`: sin permisos usa el servicio; ignora un objeto cuyo dueño no sea SYSTEM/Administradores, datos de más de 15 s, 0 °C o fuera de 1–150 °C. Aviso concreto según el caso (no activo, no responde, sin dato válido) en vez de «N/D».
- `app/Puente/SensorService.cs`: Puente registra/reapunta el servicio a la versión activa en cada instalación, actualización, Reparar y vuelta atrás; lo para antes de tocar archivos (CloseVigia), lo arranca al lanzar Vigía y lo borra al desinstalar antes que los archivos. Solo acepta rutas bajo la instalación protegida. Reinicio automático si se cae.
- Traducciones en/pt/fr/de de los avisos nuevos (`tools/i18n-add-sensors.js`).

## Pruebas ejecutadas
- Contrato estático tools/sensor-install-contract-test.py: rojo antes del cambio, verde después. No demuestra ejecución real de Setup.
- Builds Vigía y Puente finales: 0 errores, 0 warnings.
- Selftest final: 329 OK / 0 FAIL (11 nuevas de sensores).
- Puente 74/0 y release 31/0 tras el servicio.
- De punta a punta sin servicio registrado: publicador de prueba como administrador + `--probe` con token de usuario básico → **77,25 °C**, `elevated=false`. Sin publicador → aviso «no está activo… usa Reparar», sin inventar grados.
- Traducciones: 0 errores en los cuatro idiomas.
- Puente 74/0 y release mock 31/0 durante esta tanda (antes de los últimos ajustes menores de registro/diagnóstico).
- Probe local real, Ryzen 9 9900X: 62,63 °C, elevated=true. Sin instalar ni modificar drivers. No representa un Ryzen remoto ni una prueba de usuario estándar.

## BLOCKED / pendiente
- VM con UAC activo: instalación limpia, actualización, Reparar, driver ausente, fallo, reinicio pendiente y aplazamiento por juego. Hipervisor del host sigue desactivado; no se cambia sin permiso.
- La presencia del servicio todavía no prueba disponibilidad. No se ha implementado una reparación automática de un PawnIO registrado pero averiado.
- Códigos especiales de reinicio del instalador deben verificarse con el contrato oficial; por ahora solo 0 se toma como éxito y ningún fallo provoca reinicio.
- Los fallos se registran en Puente; falta un resumen persistente amigable del estado de dependencia y reintento diferido automático. Reparar permite repetir cuando se cierre el juego.
- Servicio «Sensores de Vigía»: probado solo con publicador de prueba (`--sensor-publish`, nombre `Local\…Test`); el servicio real (`sc create`, arranque como SYSTEM, `Global\VigiaSensores`) no se registró en este PC. Falta en VM: instalar, actualizar (reapunta a la nueva versión), Reparar, desinstalar (se borra antes que los archivos), reinicio del equipo y caída del servicio.
- Rechazo de una memoria compartida creada por un usuario normal: el código lo comprueba por dueño, pero solo se probó el caso «dueño ilegible», no un objeto real creado por otra cuenta.
- Endurecimiento opcional pendiente: reducir privilegios del servicio (`sc privs`) tras probar que el driver sigue abriendo.
- Las cadenas antiguas y DOM oculto de consentimiento permanecen por compatibilidad, sin emisión del aviso ni instalación desde Monitor. Falta retirar ese código muerto y revisar documentación/traducciones anteriores.
- Falta prueba final en Ryzen del amigo (modelo y versión desconocidos). No se declara arreglado ese equipo.
- No instalación real en host, no cambio de clave, no push, merge ni release.

Estado: IMPLEMENTED parcial + UNIT_TESTED; no VM_TESTED. NOT READY para publicar como solución universal de temperatura Ryzen.

## Revisión independiente del servicio (Codex, solo lectura, 29 de octubre)

Sin escalada de privilegios: el servicio no recibe órdenes ni lee datos de nadie, la ruta del ejecutable se limita a la
instalación protegida y `sc.exe` se llama con ruta fija y argumentos separados. Siete hallazgos de robustez, contrastados
uno a uno con el código (todos ciertos) y corregidos — **IMPLEMENTED, sin compilar todavía** (el usuario estaba jugando):

| # | Gravedad | Hallazgo | Corrección |
|---|---|---|---|
| 1 | Media | Si un lector mantenía abierta la memoria, el servicio reiniciado encontraba el nombre ocupado y moría | Se reutiliza solo si su dueño es SYSTEM (la dejó la copia anterior); con otro dueño se rechaza. 5 reintentos de 2 s |
| 2 | Media | El límite de 30 s de `sc.exe` se medía después de leer la salida | Lectura asíncrona de las dos salidas y límite desde el arranque |
| 3 | Media | No se comprobaba que el servicio quedara parado antes de reapuntarlo | `StopSensors()` devuelve si quedó `STOPPED` (estado por número, no por texto traducido); sin parada no se reapunta |
| 4 | Baja | Se anunciaba «activo» antes de poder publicar | `START_PENDING` → `RUNNING` solo tras crear la memoria; Puente confirma `RUNNING` |
| 5 | Baja | Cualquier proceso llamado «Vigia» activaba la lectura | Solo cuenta una Vigía de la misma instalación |
| 6 | Baja | Objetos `Process` sin liberar | Se liberan todos |
| 7 | Baja | El aviso de Monitor no cambiaba con el estado del servicio | Se recalcula en cada lectura |

Además: si `Computer.Open()` falla, se cierra lo abierto a medias.

**Pendiente:** `CloseVigia` (Puente) sigue adelante aunque el servicio no se pare; el reemplazo de archivos fallaría y lo
recoge la recuperación existente, pero no está probado en VM. Pruebas nuevas en el selftest: reutilizar el nombre del mismo
dueño; rechazar en el publicador y en el lector un objeto creado por otro dueño.
