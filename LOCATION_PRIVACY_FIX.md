# Aviso «Location has been turned off» con Vigía — causa y arreglo

Fecha: 2026-10-30. Rama `debloat-v2`. Commits: `1ea871f` (ubicación), `fea4ae0` (barrera de versión de Puente).

## Causa raíz (verificada)

En 0.20.17 la pestaña **Sugerencias** analiza la red. Si la conexión principal es Wi‑Fi, `Suggestions.cs` llama a
`Wlan.Read(main.Id)` (`v0.20.17:app/Vigia/Suggestions.cs:157`), que hace:

| Línea (v0.20.17) | Llamada | Para qué |
|---|---|---|
| 207 | `WlanQueryInterface(..., OpCurrentConnection = 7, ...)` | nombre de la red (SSID), calidad, PHY, velocidad |
| 226 | `WlanGetNetworkBssList(...)` | redes vecinas en el mismo canal (`SameChannel`) |

Microsoft Learn, «Changes to API behavior for Wi‑Fi access and location», sección «Which APIs will be affected?»:
sin consentimiento de ubicación precisa, `WlanGetNetworkBssList` y `WlanQueryInterface` con
`wlan_intf_opcode_current_connection` devuelven `ERROR_ACCESS_DENIED`, y la primera llamada muestra un aviso del
sistema y aparece en la actividad reciente de ubicación. Ese es el aviso que vio el usuario.

No hacía falta pulsar nada: la interfaz lanza la revisión sola **20 s después de abrir Vigía** y al volver a la ventana
(`v0.20.17:app/Vigia/wwwroot/app.js:422–424`, `sugAuto`).

Descartado (búsqueda en código y en binarios): `navigator.geolocation` en la interfaz (no existe), `Windows.Devices.Geolocation`,
`Windows.Devices.WiFi`, `GetLanIdentifiers`, `WlanScan`, `WlanGetAvailableNetworkList`, `WlanRegisterNotification`,
`netsh wlan`, capacidades `location`/`wiFiControl`. Solo `Vigia.dll` importa `wlanapi`; ninguna dependencia lo hace.
Monitor, CPU, GPU, RAM y temperaturas no tocan Wi‑Fi ni ubicación.

## Arreglo

- **Wi‑Fi sin ubicación** (`Suggestions.cs`, clase `Wlan`): solo `WlanQueryInterface` con tiempo real (19), RSSI
  (`0x10000102`) y canal (8), en una lista cerrada `Allowed` que lanza si se pide otra. Nada de SSID, BSSID ni redes
  vecinas; `WifiFacts` ya no tiene esos campos. Si Windows deniega (rc 5), devuelve sin datos y no reintenta.
  La sugerencia de «redes en el mismo canal» desaparece; la de 2,4 GHz se mantiene con la banda.
- **WebView2** (`LocationGuard.Harden`): las tres instancias (interfaz, descarga de Microsoft, sonda) responden
  `Deny` a cualquier permiso de página sin mostrar diálogo. Vigía no tiene ninguna función que necesite ubicación, así
  que no hay flujo de consentimiento que añadir.
- **Trazas**: `privacidad.log` en la carpeta de datos registra qué se pidió y el código de retorno (p. ej.
  `wifi: lectura sin ubicación (tiempo real rc=…, rssi rc=…, canal rc=…, datos=sí/no)` y los permisos WebView2
  denegados). Nunca red, BSSID, coordenadas, frecuencia ni señal (lo comprueba `tools/location-test.js`).
- **Diagnóstico**: `Vigia.exe --geo-probe <salida>` (WebView2 oculto pide `navigator.geolocation`, espera error 1) y
  `--wifi-check <salida>`.
- No se cambió la configuración de ubicación de Windows ni ninguna política.

## Verificación (este Windows, ubicación apagada)

Estado leído sin modificar: `ConsentStore\location` (HKLM) = `Deny`, sin política `LocationAndSensors`, `lfsvc`
detenido, ninguna entrada de Vigía en `NonPackaged`, build 26300.

| Prueba | Resultado |
|---|---|
| `--selftest` | 371 OK / 0 FAIL (9 nuevas de ubicación) |
| `tools/location-test.js` con sonda real | 25 OK / 0 FAIL; `navigator.geolocation` → error 1 |
| `tools/location-test.js` sobre el código de v0.20.17 | FAIL (detecta `WlanGetNetworkBssList`, current_connection, SSID/vecinos, falta de `Harden`) |
| `puente-test` / `release-test` / `signing-test` / `i18n-check` | 76/0 · 31/0 · 39/0 · 2435 claves, 0 errores |

**Pendiente (hardware):** este PC no tiene ningún Wi‑Fi conectado (16 adaptadores inalámbricos en `Down`), así que la
lectura de calidad/RSSI/banda con una red real conectada no se ha visto aquí. Hay que confirmarla en un equipo con Wi‑Fi
y la ubicación apagada: abrir Sugerencias y comprobar que no aparece el aviso y que `privacidad.log` dice `datos=sí`.

Relectura del 30-10 (noche, 2.ª), solo lectura con `Get-NetAdapter -IncludeHidden` (sin `netsh wlan`, que también
pide ubicación): 3 adaptadores Wi‑Fi, los 3 `Disconnected`; lo único conectado es Ethernet. **Sigue `HARDWARE_VALIDATION_PENDING`**:
no hay equipo de prueba con Wi‑Fi conectado y no se inventa el resultado. Prueba exacta cuando lo haya (ubicación de
Windows apagada, sin tocar políticas):

1. Instalar el candidato (o copiar `Vigia.exe` del zip) y lanzar con datos aislados: `set VIGIA_DATA=%TEMP%\vigia-wifi`.
2. `Vigia.exe --wifi-check %TEMP%\wifi.txt`: el adaptador conectado debe salir `estado=Up datos=sí` (el comando no imprime la señal a propósito). Luego, en Sugerencias de red, comprobar que la señal mostrada cuadra con la de Windows (icono de red / Configuración → Wi‑Fi) y que no se muestra el nombre de la red.
3. Abrir Vigía → Sugerencias de red y esperar 30 s: **no** debe salir «Location has been turned off» / «Una app quiere usar tu ubicación».
4. `%TEMP%\vigia-wifi\privacidad.log`: `datos=sí` y ninguna línea con nombre de red, BSSID, MAC ni coordenadas.
5. Configuración → Privacidad → Ubicación → «Actividad reciente»: Vigía no aparece.

## Puente: ¿compara versiones o hashes?

`app/Puente/Setup.cs`, `PlacePuente` (454–479), llamado en cada instalación (270):

1. Si la versión instalada es **igual o mayor** que `puenteVersion` del manifiesto firmado → no hace nada (466–470).
   No mira el hash del instalado.
2. Si es menor → baja `Puente.exe`, `FetchVerified` comprueba su **SHA‑256** contra el manifiesto firmado, exige
   `FileVersion == puenteVersion` (474), renombra el viejo a `Puente.old.exe` y se relanza.

Por tanto un Puente con código distinto y el mismo número **no se instala nunca**. `tools/release.sh` ahora se niega a
publicar si `app/Puente` cambió desde la última etiqueta con el mismo `<Version>` (`fea4ae0`). `preparar-relevo.js`
sube la versión de Puente en `anadir` y en `promover` (2.0.1 → 2.0.2 → 2.0.3 en las pruebas).

Vigía 0.20.17 (`v0.20.17:app/Vigia/Updates.cs`) baja Puente de la última publicación, comprueba su firma con la clave
pública incluida y lo lanza; el Puente que reciba hace la migración.

Relevo de punta a punta desde 0.20.17: `VM_VALIDATION_REPORT.md`, fila 40.

## Revisión 31-10-2026 — lectura Wi‑Fi apagada por defecto (commit `f243c4b`)
- **Por qué:** según la documentación de WLAN de Microsoft, las consultas de la conexión actual y de las redes cercanas pueden pedir permiso de ubicación desde Windows 11 24H2. Aquí no hay hardware para demostrar lo contrario (3 adaptadores Wi‑Fi, todos `Disconnected`). Por tu regla, lo no demostrado queda apagado.
- **Qué cambia:** `Suggestions.cs` ya no llama a `Wlan.Read` salvo `Wlan.SignalOn` (solo con `VIGIA_WIFI_SIGNAL=1`, para la prueba física). Apagada, Vigía no abre `wlanapi` y deja en `privacidad.log` la línea «wifi: lectura de señal apagada en esta versión (sin consultas WLAN)», sin nombres de red, BSSID, MAC ni coordenadas. Se pierden en 0.21.0 calidad, RSSI, banda, PHY y velocidad del Wi‑Fi; las sugerencias de red por cable y DNS no cambian.
- **WebView2:** `LocationGuard.Harden` deniega `PermissionRequested` de geolocalización (sin cambios desde `1ea871f`).
- **Pruebas (build de `f243c4b`, `scratch/p10/vb/Vigia.exe`, 0 avisos, 0 errores):** `location-test.js` **29 OK / 0 FAIL** (incluye: sin `WlanGetNetworkBssList`, sin `geolocation`/Geolocator, barrera presente, `privacidad.log` sin SSID/BSSID/MAC/coordenadas). `candidate-check.js` ahora exige la barrera en el `Vigia.exe` del candidato.
- **`--selftest`** de ese build con datos aislados: 7 FAIL, todos en el bloque de juegos (se niega a escribir perfiles con un juego abierto). El selftest completo se repite en `release.sh` al regenerar el candidato, con el juego cerrado.
- **Sigue `HARDWARE_VALIDATION_PENDING`:** falta la prueba de arriba (pasos 1–5) en un equipo con Wi‑Fi conectado y la ubicación de Windows desactivada, lanzando con `VIGIA_WIFI_SIGNAL=1`. Si pasa y no sale el aviso, se puede encender en una versión posterior. No se modificó la ubicación de Windows ni ninguna política.
