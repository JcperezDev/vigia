# Perfiles de juegos — evaluación
Fecha: 2026-10-29 · `debloat-v2`. Solo investigación web documentada; no se leyó ni modificó ningún archivo de juego del PC.

Niveles: **OFFICIAL** (editor/desarrollador o wiki de Valve), **CORROBORATED** (2+ fuentes independientes que coinciden), **RUMOR** (una sola fuente: excluido).

| Juego | Evaluación | Archivo | Anti-trampa | Decisión 0.21.0 |
|---|---|---|---|---|
| Valorant | REJECTED | `%LOCALAPPDATA%\VALORANT\Saved\Config\` (OFFICIAL) | Vanguard (kernel) | Regla del propietario: nunca se edita. Además se sincroniza con el servidor |
| Marvel Rivals | REJECTED | `%LOCALAPPDATA%\Marvel\Saved\Config\Windows\GameUserSettings.ini` | NetEase | NetEase prohíbe oficialmente modificar los `.ini` de gráficos y lo sanciona (marvelrivals.com/guide/20250226/41348_1214569.html) |
| Counter-Strike 2 | VERIFIABLE_CANDIDATE | `<Steam>\userdata\<SteamID3>\730\local\cfg\cs2_video.txt` (CORROBORATED) | VAC | No se implementa |
| Rocket League | VERIFIABLE_CANDIDATE | `Documents\My Games\Rocket League\TAGame\Config\TASystemSettings.ini` (CORROBORATED; carpeta OFFICIAL) | Easy Anti-Cheat | No se implementa |
| Apex Legends | VERIFIABLE_CANDIDATE | `Saved Games\Respawn\Apex\local\videoconfig.txt` (CORROBORATED) | Easy Anti-Cheat | No se implementa |
| Rainbow Six Siege X | RESEARCH_PENDING | `Documents\My Games\Rainbow Six - Siege\<perfil>\GameSettings.ini` (OFFICIAL) | BattlEye | No se implementa |

## Claves con evidencia (solo valores observados)
- **CS2** (`cs2_video.txt`, KeyValues `"video.cfg"`): `setting.mat_vsync` 0/1, `setting.r_low_latency` 0/1-2 (semántica de 2 sin confirmar), `setting.videocfg_shadow_quality`, `setting.videocfg_particle_detail`, `setting.videocfg_dynamic_shadows` (escalas sin documentar). `fps_max` es cvar de consola, no del archivo.
- **Rocket League** (`[SystemSettings]`): `UseVsync`, `MotionBlur`, `DynamicShadows` True/False; `DetailMode` 0–2. Epic confirma que los ajustes de vídeo son solo locales (help a000084283).
- **Apex** (`"VideoConfig"`): `setting.mat_vsync_mode`, `setting.csm_enabled`, `setting.shadow_enable`, `setting.volumetric_lighting`, `setting.ssao_enabled`; solo se observó el valor 0. FPS: opción de lanzamiento `+fps_max` (help.ea.com, OFFICIAL).
- **Siege X**: `FPSLimit` en `[DISPLAY]` es OFFICIAL (Ubisoft); `VSync`, `MotionBlur`, `Shadow`, `LensEffects` solo con muestras de 2018-2019, sin confirmar tras Siege X.

## Por qué no se implementa ninguno en 0.21.0 (decisión, no bloqueo indefinido)
1. **Sin prueba propia reproducible.** La regla para admitir un perfil exige comprobar versión, ruta, claves, copia y restauración contra un archivo real generado por el juego. Rocket League y Apex no están instalados en ningún equipo de prueba; CS2 está en el disco de juegos del PC, que no se toca. Escribir claves que no se vieron en un archivo real sería inventar.
2. **CS2:** la carpeta `730\remote` va por Steam Cloud y el juego reescribiría el archivo al salir (fuente única): una restauración exacta no está garantizada.
3. **Apex:** solo hay valores «apagado»; un moderador de EA desaconseja tocar archivos para controlar ajustes. Lo único oficial (`+fps_max`) es una opción de lanzamiento, no un archivo.
4. **Rocket League** es el mejor candidato (INI local, vídeo solo local según Epic). Para la siguiente versión: generar el archivo con el juego instalado en un equipo autorizado, verificar las cuatro claves y reutilizar el flujo de Fortnite (juego cerrado, copia, vista previa, cambio mínimo, verificación, restauración exacta).
5. **Siege X** pasa a candidato cuando haya un `GameSettings.ini` de 2025+ con las claves.

El código existente se mantiene: `b560ecf` limita la restauración al INI permitido de Fortnite y no da por aplicado un perfil sin claves reconocidas. Fortnite sigue siendo el único perfil.

Reglas comunes si se añade un perfil: juego y launcher cerrados, copia byte a byte antes de escribir, restauración exacta, nunca marcar archivos como solo lectura, ningún proceso, memoria ni componente anti-trampa tocado.
