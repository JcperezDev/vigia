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

## Pruebas ejecutadas
- Contrato estático tools/sensor-install-contract-test.py: rojo antes del cambio, verde después. No demuestra ejecución real de Setup.
- Builds Vigía y Puente finales: 0 errores, 0 warnings.
- Selftest final: 318 OK / 0 FAIL.
- Puente 74/0 y release mock 31/0 durante esta tanda (antes de los últimos ajustes menores de registro/diagnóstico).
- Probe local real, Ryzen 9 9900X: 62,63 °C, elevated=true. Sin instalar ni modificar drivers. No representa un Ryzen remoto ni una prueba de usuario estándar.

## BLOCKED / pendiente
- VM con UAC activo: instalación limpia, actualización, Reparar, driver ausente, fallo, reinicio pendiente y aplazamiento por juego. Hipervisor del host sigue desactivado; no se cambia sin permiso.
- La presencia del servicio todavía no prueba disponibilidad. No se ha implementado una reparación automática de un PawnIO registrado pero averiado.
- Códigos especiales de reinicio del instalador deben verificarse con el contrato oficial; por ahora solo 0 se toma como éxito y ningún fallo provoca reinicio.
- Los fallos se registran en Puente; falta un resumen persistente amigable del estado de dependencia y reintento diferido automático. Reparar permite repetir cuando se cierre el juego.
- Privilegios: ya existe telemetría por broker cuando está vivo. Falta probar apertura del dispositivo como usuario estándar y, si resulta necesaria, diseñar/validar una ruta de lectura privilegiada sin elevación permanente. No se añadió un servicio nuevo sin esa evidencia.
- Las cadenas antiguas y DOM oculto de consentimiento permanecen por compatibilidad, sin emisión del aviso ni instalación desde Monitor. Falta retirar ese código muerto y revisar documentación/traducciones anteriores.
- Falta prueba final en Ryzen del amigo (modelo y versión desconocidos). No se declara arreglado ese equipo.
- No instalación real en host, no cambio de clave, no push, merge ni release.

Estado: IMPLEMENTED parcial + UNIT_TESTED; no VM_TESTED. NOT READY para publicar como solución universal de temperatura Ryzen.
