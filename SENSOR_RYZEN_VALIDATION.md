# Validar la temperatura del procesador en el Ryzen del propietario: procedimiento

Estado: **HARDWARE_VALIDATION_PENDING**. Esta prueba no se ha hecho; este documento solo explica cómo hacerla.
Hasta que el propietario la ejecute y guarde la salida, ningún informe puede decir que Vigía lee bien la temperatura en
el AMD Ryzen 9 9900X.

## Qué se comprueba

1. Con permisos de administrador y el driver PawnIO presente, Vigía lee un valor real del procesador (sensores
   `Core (Tctl/Tdie)` / `CCD1 (Tdie)` / `CCD2 (Tdie)`) dentro de un rango creíble (entre 25 y 95 °C).
2. El valor cambia con la carga, y no es fijo ni 0.
3. El valor cuadra (±5 °C) con la temperatura que muestra la pantalla de monitor de la BIOS. **Solo se mira; no se
   cambia nada en la BIOS.**
4. Sin lectura válida (sin permisos o sin PawnIO), Vigía dice «Temperatura de CPU no disponible.». Esto ya se
   comprobó en VigiaLab el 30-10-2026, y en el PC solo se repite si sale gratis.

## Qué NO hace

- No instala el servicio «Sensores de Vigía» ni ningún otro servicio (la compilación pública no lo trae).
- No instala ni actualiza Vigía y no toca la 0.20.17 instalada: el candidato se ejecuta desde una carpeta suelta.
- No instala drivers. PawnIO **ya está instalado** en este PC (servicio `PawnIO`, inicio manual, en marcha al
  comprobarlo el 30-10-2026). Si no lo estuviera, la prueba se para ahí: instalarlo requiere tu permiso.
- No escribe en la BIOS ni cambia EXPO, UAC ni Hyper-V.
- No usa los datos reales de Vigía: `VIGIA_DATA` apunta a una carpeta temporal.

## Pasos (PowerShell normal; tardan unos 10 minutos)

0. **Cierra todos los juegos y sus antitrampas** (Valorant y Vanguard incluidos) y cierra Vigía 0.20.17 desde la bandeja,
   para que no haya dos lecturas a la vez. Volverás a abrirla en el paso 6.
1. Carpeta suelta con el candidato (ni instalado ni firmado):
   ```powershell
   $d = "$env:TEMP\vigia-sensores-$(Get-Date -f yyyyMMdd-HHmm)"
   Expand-Archive "C:\Users\Pickle OS\work\vigia\out\0.21.0\Vigia-0.21.0-win-x64.zip" "$d\app"
   $env:VIGIA_DATA = "$d\datos"
   (Get-Service PawnIO).Status          # debe decir Running o Stopped; si da error, PARA aquí
   ```
2. Lectura en reposo (espera 2 minutos sin hacer nada):
   ```powershell
   & "$d\app\Vigia.exe" --probe "$d\reposo.json"; $LASTEXITCODE
   $j = Get-Content "$d\reposo.json" -Raw | ConvertFrom-Json
   "elevado=$($j.snapshot.Elevated)  cpu=$($j.snapshot.Cpu.Temp)"
   $j.sensors | Select-String 'Tctl|Tdie|CCD'
   ```
   Lo esperado es `elevado=True` (con UAC desactivado, tu consola ya es de administrador) y un valor de `cpu` entre 25 y 60.
3. Lectura con carga: abre otra PowerShell y deja 4 hilos ocupados durante 60 s:
   ```powershell
   1..4 | % { Start-Job { $f=(Get-Date).AddSeconds(60); while((Get-Date) -lt $f){} } } | Out-Null
   ```
   A los 40 s, en la primera consola: `& "$d\app\Vigia.exe" --probe "$d\carga.json"` y repite las dos líneas de
   lectura. Lo esperado es un valor mayor que en reposo y por debajo de 95.
   Al terminar: `Get-Job | Remove-Job -Force`.
4. Contraste con la BIOS: en el próximo reinicio que hagas por tu cuenta, entra en la pantalla de monitor de hardware
   de la BIOS, apunta la temperatura del procesador y sal **sin guardar**. La lectura en reposo del paso 2 debería
   estar a menos de 5 °C. Esto es opcional: la BIOS mide en otro estado de carga.
5. La interfaz: `& "$d\app\Vigia.exe"`, ve a Monitor, comprueba que la tarjeta del procesador muestra grados (no «N/D»)
   y que la gráfica se mueve. Cierra la ventana.
6. Limpieza: `Remove-Item $d -Recurse -Force` y `Remove-Item Env:VIGIA_DATA`. Vuelve a abrir tu Vigía 0.20.17 desde
   el menú Inicio. No queda nada instalado.

## Qué guardar como evidencia

Copia `reposo.json` y `carga.json` (no llevan claves ni datos personales, solo lecturas del sistema) a
`work\vigia\evidencia\ryzen-<fecha>\` antes del paso 6, o pégalos y yo actualizo los informes.

| Resultado | Qué significa |
| --- | --- |
| Valor en reposo entre 25 y 60, mayor con carga | **Validado.** Pasa a LISTO en los informes |
| `cpu` vacío con `elevado=True` y PawnIO en marcha | Fallo de lectura en este procesador. Sigue pendiente; hay que revisar los nombres de sensor |
| 0, valor fijo o mayor de 95 | Valor no creíble. Vigía no debe mostrarlo (filtro `realTemp`); hay que revisar el filtro |
| `elevado=False` | La consola no tenía permisos. Repite con «Ejecutar como administrador» |
