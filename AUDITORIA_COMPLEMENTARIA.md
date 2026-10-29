# Auditoría complementaria — Vigía

Segunda pasada sobre la auditoría externa de la versión 0.20.17.

- **Commit de partida:** `b3453cd` (0.20.17).
- **Commit final con código:** `167e52b`, en la rama local `desarrollo-auditoria`. Este informe va en el commit siguiente.
- **Sin push, sin merge a `main` y sin release.** Nada de esto está publicado.
- `git diff --stat b3453cd..167e52b`: 51 archivos, +3610 / −794.

Cada hallazgo se contrastó con el código antes de tocarlo. Si algo no se pudo probar en una máquina real, se dice.

---

## 1. Estado de cada punto de entrega (fase 13)

| # | Criterio | Estado | Cómo se comprobó |
|---|---|---|---|
| 1 | Toolbox sin rutas personales | ✅ | `Toolbox.cs` reescrito (`eda0025`); `grep` sin rutas de carpetas personales; `ToolsRoot` eliminado |
| 2 | Startup sin mensajes de una máquina concreta | ✅ | `eda0025`; `grep` de Hermes/Memurai/OpenRGB/TimerResolution/remoting_host sin resultados (la única coincidencia es la API `NtQueryTimerResolution`) |
| 3 | Filtro de antitrampas sin subcadenas | ✅ | Nombre exacto de proceso o servicio; el selftest prueba Vanguard, BattlEye y EAC, y descarta `svgconvert.exe` y `myvgcapp.exe` |
| 4 | Optimización de discos accesible | ✅ | `aee2796`: dentro de Almacenamiento; análisis por WMI (`Win32_Volume.DefragAnalysis`); tarea semanal leída desde el XML; prueba real en D: |
| 5 | RAM sin operaciones agresivas inesperadas | ✅ | Interfaz, bandeja, modo simple y automático usan `MemOptions.Safe` (solo la caché en espera de baja prioridad); lo agresivo, solo marcándolo a mano; el automático no actúa con un juego abierto |
| 6 | Métricas de memoria explicadas | ✅ | Caché descartada, memoria libre ganada y modificada escrita, por separado; «Liberar RAM no da más FPS» |
| 7 | HAGS y otros, bien clasificados | ✅ | `739b539`: HAGS opcional y «mide con Frametimes»; los apagados sin evidencia salen como `info`, no como aviso |
| 8 | Sin textos fijos para NVIDIA o Ryzen | ✅ (ver riesgos) | Los textos de NVIDIA solo salen con tarjeta NVIDIA; la resolución personalizada indica también la ruta de AMD e Intel; `gpu-high` sin marcas |
| 9 | SMB1: cliente y servidor | ✅ | Se lee el servidor y el componente opcional del cliente; no dice «SMB1 apagado» si el cliente sigue instalado |
| 10 | Ubicación separada del historial | ✅ | `activity-off` ya no toca `location`/`lfsvc`; `location-off` va aparte, experimental y fuera del modo simple |
| 11 | Windows Update explica su alcance | ✅ | `update-control` (reinicios) separado de `wu-drivers-off` (`ExcludeWUDriversInQualityUpdate`, todos los drivers por WU, experimental) |
| 12 | Fallos de parsing | ✅ | `3880140`: `Health` (`TryParse`), `Updates` (`v`, `-beta`, `-rc`), `Debloat` (JSON roto o vacío), `bcdedit` en 5 idiomas, `schtasks` por XML |
| 13 | Restauraciones parciales identificadas | ✅ | Si un ajuste falla a mitad, se deshace; `Backups` avisa de lo que no garantiza (apps quitadas, pantalla desconectada) |
| 14 | ISO sin cuentas sin contraseña | ✅ | `ce674ee`: la ISO no crea cuentas ni guarda nombre o contraseña; los pide Windows en su pantalla |
| 15 | Apps útiles nunca se quitan solas | ✅ | `5ab2475` (ver §3) |
| 16 | Limpieza de shaders manual y con aviso | ✅ | `02590e0` |
| 17 | Perfiles nuevos de juegos con copia | ❌ **No hecho** | Sin archivos reales para verificar el formato (ver §5) |
| 18 | FPS distingue APIs no soportadas | ✅ | `588af1b`: DXGI + DX9; con Vulkan u OpenGL dice «no se pudo medir» en vez de dar 0 |
| 19 | Centro de seguridad más allá de Defender | ✅ | `5daf6db`: alteraciones, SmartScreen, UAC, firewall por perfil, integridad de memoria, WU, arranque seguro, TPM, BitLocker, exclusiones, directivas; solo lectura |
| 20 | Intel y AMD considerados | ⚠️ Parcial | ReBAR solo se verifica en NVIDIA; con AMD e Intel dice que no se puede comprobar y que mires la BIOS. Sin hardware AMD/Intel para probar |
| 21 | Ayuda con las secciones reales | ✅ | `02590e0`: reescrita a partir del menú real (`data-view`) |
| 22 | Cinco idiomas | ✅ | `i18n-check`: 2392 claves, 0 errores; capturas en en/de/fr/es |
| 23 | Pruebas automatizadas | ✅ | Selftest con 165 comprobaciones + CI (ver §6) |
| 24 | Compila | ✅ | Release `win-x64`, exit 0 |
| 25 | Sin regresiones | ✅ en lo automatizado | Selftest 165/0; falta validación física (§8) |

---

## 2. Hallazgos de la auditoría externa

### Confirmados y corregidos
- Toolbox con rutas personales (`12- Apps\...`, `7- AMD OR NVIDIA\DDU\...`, `.nip`) → eliminadas. Queda un catálogo de herramientas de Windows y 9 apps de WinGet, instaladas solo tras confirmar.
- Startup con explicaciones de una máquina concreta y filtro `id.Contains("vgc")` → explicaciones genéricas y nombres exactos.
- `view-opt` huérfana → integrada en Almacenamiento.
- `Health.cs`: `int.Parse(DeviceId)` → `TryParse`.
- `Updates.cs`: `tag_name` frágil → tolerante; las prereleases nunca entran en el canal estable.
- `Debloat.cs`: JSON de PowerShell sin protección → `ParseInstalled`, que nunca lanza excepciones.
- `Optimize.cs`: dependía de textos localizados de `defrag`/`schtasks` → WMI + XML de la tarea.
- `Backups.cs`: `hz` y `appx` → `hz` solo se restaura si la pantalla sigue conectada; para `appx` se avisa de que no hay restauración garantizada.
- `IsoBuild.cs`: cuenta local sin contraseña → eliminada.
- `Tweaks.cs`: `dynamictick`, `FindUltimate()`, `UltimateName`, `BitLockerOn()` sin uso → eliminados. **No** se añadió dynamic tick al catálogo.
- HAGS, `gpu-high`, SMB1, `telemetry-off`, `activity-off`, `update-control`: confirmados y corregidos (tabla de arriba).
- `Fps.Known`: Marvel Rivals → se detecta el proceso del juego, no el launcher.

### Ya cubiertos por la primera pasada
- CI (`.github/workflows/ci.yml`) ya existía. Esta vez se añadió el escaneo de secretos.
- README contradictorio sobre drivers de terceros (PawnIO) → corregido en la primera pasada.
- `Tray.cs` con `new MemOptions()`: en el código actual ya era `MemOptions.Safe`, cuyos valores por defecto son conservadores. Se añadió que «Optimizar» desde la bandeja no haga TRIM con un juego abierto.
- Métricas de memoria divididas: ya hechas en la primera pasada.

### Incorrectos o inexactos
- `Diagnostics.cs` y `bcdedit`: los nombres de opción de `bcdedit` no cambian con el idioma, solo los valores (`Yes`/`Sí`/`Ja`/`Oui`/`Sim`). La lectura ya estaba basada en eso; se añadieron pruebas para los 5 idiomas.
- «Textos NVIDIA/Ryzen obligatorios» en `gpu-high`: el texto se corrigió, pero la mayoría de menciones a NVIDIA en `Rig.cs`, `Display.cs` y `GpuModes.cs` ya dependían de detectar una tarjeta NVIDIA.

---

## 3. Hallazgos nuevos de esta pasada (no venían en el informe externo)

1. **Modo simple podía quitar todas las apps.**
   - Dos variantes:
     - Sin lista, el backend leía `KeepApps = null` como «quitar todo».
     - Ya corregido eso, si la lista de apps no llegaba a dibujarse (la respuesta del catálogo aún no había llegado), se mandaba `keepApps: []`, que también significaba «quitar todo».
   - Ahora el frontal manda `removeApps`, solo con lo que el usuario desmarcó, y una lista vacía no quita nada.
   - Afectaba a versiones no publicadas de la rama; 0.20.17 no tenía el modo simple con lista.
2. **La ISO premarcaba Copilot, Alarmas, Teams, Spotify, etc.** Ahora esas apps llevan la marca «útil» y no se marcan solas en Apps, en el modo simple ni en la ISO.
3. **Paquetes framework:** `Remove` filtra `IsFramework` y `NonRemovable` antes de quitar.
4. **Windows Update pausado hasta 2038** (lo detectó el nuevo Centro de seguridad en el PC de desarrollo). Vigía lo marca como aviso y no lo cambia.
5. **Advertencias de limpieza sin traducir:** se partían junto a la ruta. Ahora van en su propia línea y se traducen.
6. **Orden de inicialización estática en `Cleanup.cs`:** lo cazó el propio selftest y está corregido.

---

## 4. Tweaks evaluados y no implementados

| Candidato | Decisión | Motivo |
|---|---|---|
| `copilot-off` | No | Copilot es ahora una app de la Store; la directiva antigua no aplica a la app nueva. Se quita desde Apps (opcional y nunca premarcada) |
| `feedback-off`, `tailored-off`, `input-personalization-off`, `lang-list-off`, `start-recommendations-off` | Pendiente | Falta verificar cada clave en Windows 11 actual y en las distintas ediciones. Ya existen `suggestions-off`, `start-web-off`, `recall-off`, `ad-id-off` y `telemetry-off` |
| `fso-off` | No | Ya existe `windowed-opt` como ajuste opcional; desactivar FSO en todo el sistema sin un problema concreto no tiene respaldo |
| `mpo-off` | No | Solo tiene sentido ante parpadeos reproducibles; sin diagnóstico no se ofrece |
| `core-parking-off` | No | Perjudica la planificación de las CPU modernas (núcleos híbridos, CCD) |
| `dynamictick` | No | Código eliminado; sin evidencia de mejora |
| `last-access-off` | No | Sin relación demostrada con juegos |
| `disk-never-off` | No | Solo con un problema diagnosticado |
| `nvidia-telemetry-tasks` | No | Faltan las dependencias entre tareas de actualización, telemetría y servicios del driver |
| Grupo C (HPET, TcpAckFrequency, mitigaciones, Defender, UAC, HVCI, limpieza periódica de standby, WU off, RealTime, colas HID, BCD) | No, nunca | Instrucción explícita |

**Apps de la fase 6:** solo se añadió **Asistencia rápida** (`MicrosoftCorporationII.QuickAssist`), cuyo ID documenta Microsoft Learn. *Correo y Calendario, LinkedIn, Traductor de Bing, Network Speed Test, Whiteboard y Journal* **no** se añadieron: la lista oficial de Microsoft (directiva `RemoveDefaultMicrosoftStorePackages`) no los incluye y no están instalados en este PC para verificar el ID. Solo había fuentes de terceros.

---

## 5. Perfiles de juegos (no implementados)

- **Valorant:** excluido a propósito, con antitrampas a nivel de kernel (Vanguard). Se detecta para FPS, pero no se edita.
- **CS2, Rocket League, Apex:** no están instalados en el PC de desarrollo; sin un archivo real no se puede verificar el formato. No se escribieron perfiles con parámetros supuestos.
- **R6 Siege, Overwatch 2, Call of Duty:** igual; CoD está en E: pero sin carpeta de configuración.
- **Marvel Rivals:** solo se corrigió la detección del proceso (juego frente a launcher).
- El perfil existente (Fortnite) mantiene copia previa, escritura atómica, conservación de claves desconocidas y bloqueo con el juego abierto. Es el modelo para los siguientes.

---

## 6. Pruebas ejecutadas

- **Build Release `win-x64`:** exit 0 (último: b28).
- **Selftest** (`Vigia.exe --selftest`, datos aislados en `VIGIA_DATA`): **OK=165 FAIL=0**. Incluye:
  - catálogo de ajustes sin IDs repetidos y migraciones de deshacer;
  - modo simple (nada experimental, sin apps sin lista, sin Xbox, sin shaders ni Papelera);
  - apps útiles nunca premarcadas;
  - parsing de PowerShell, versiones semánticas y `bcdedit` en 5 idiomas;
  - antitrampas por nombre exacto;
  - ISO sin cuentas;
  - SMB1, Optimize (análisis real de D:) y percentiles de FPS.
- **Puente `--zip-test`:** 7/7; paquete real 0.20.17 321/321 con firma válida.
- **`node tools/i18n-check.js`:** 0 errores en en/pt/fr/de. Las «distintas» que quedan son cadenas internas del selftest, que no se traducen a propósito.
- **`node --check`** de `app.js` e `i18n.js`.
- **Escaneo de secretos** (`git grep` del CI) en local: sin coincidencias.
- **`winget show --exact`** de los 9 IDs: todos existen. Editores: CPUID, TechPowerUp, REALiX, MSI, Remi Mercier, Wagnardsoft, Orbmu2k, Sysinternals ×2.
- **`--fps-probe`:** 962 Present DXGI = 962 eventos del kernel.
- **Capturas con volcado de texto (`--snap`):** Almacenamiento en en/de/es sin desbordes; Seguridad en en/de; Ajustes en en/es con la detección real del PC (dos GPU, edición Pro).

**CI** (`ci.yml`, `permissions: contents: read`, sin publicar nada):
- compila Vigía y Puente;
- revisa paquetes vulnerables y secretos;
- valida JS e i18n y que `docs/TWEAKS.md` esté al día;
- ejecuta zip-test y el selftest.

**No se ha ejecutado nunca en GitHub**, porque no hay push.

---

## 7. Archivos modificados (desde `b3453cd`)

- **Código y web:** `app/Vigia/{Backups,Bios,Cleanup,Debloat,Diagnostics,Fps,Health,IsoBuild,Memory,Optimize,Pawn,Program,Rig,Security,Settings,Simple,Startup,Support,Toolbox,Tray,Tweaks,UiMessage,Updates}.cs`, `Vigia.csproj`, `tweaks.json`, `wwwroot/{app.js,app.css,i18n.js,index.html}`, `wwwroot/i18n/*.json`, `app/Puente/Program.cs`.
- **Docs:** `README.md`, `README.es.md`, `PRODUCT.md`, `SECURITY.md`, `docs/{SECURITY-DESIGN,TOOLS,TWEAKS}.md`, `.github/workflows/ci.yml`.
- **Herramientas:** `tools/{i18n-add-audit,tweaks-doc,tweaks-review,stats}.js`, `tools/release.sh`.
- **Landing y soporte:** `landing/{index.html,main.js}`, `support.json`.

---

## 8. Necesita validación física

- Herramientas abiertas **sin** permisos de administrador: el PC de desarrollo tiene el UAC desactivado y la prueba no fue concluyente. Hay que probar con UAC activo.
- FPS y frametimes en juegos reales, en DX11, DX12 y DX9, y el mensaje «no se pudo medir» con Vulkan u OpenGL.
- Aplicar y deshacer ajustes reales (HAGS, `gpu-high` en un equipo con dos GPU, SMB1, ubicación, drivers de WU).
- Modo simple y Apps quitando apps reales; ISO instalada en una VM con la pantalla de cuenta de Windows.
- Limpieza de shaders con NVIDIA y AMD reales; tarea semanal de optimización en D:/E:.
- Centro de seguridad en un equipo con UAC, Defender y BitLocker en estados distintos, y en un equipo gestionado por directivas.
- ReBAR y temperaturas en AMD e Intel; PawnIO en un PC limpio; actualización completa con el Puente nuevo.

## 9. Riesgos y tareas pendientes

- **Perfiles de juegos nuevos:** no hechos (§5).
- **Grupo A de privacidad:** pendiente de verificar clave por clave (§4).
- **ReBAR en AMD e Intel:** «No verificado», sin método del fabricante integrado.
- **CI:** sin probar en GitHub hasta que se autorice el push.
- **Decisiones del informe anterior todavía sin respuesta:**
  - mover la instalación a Program Files;
  - fusionar `releases-en-vigia` y `desarrollo-auditoria` en `main`;
  - publicar 0.20.18;
  - nivel de elevación del `app.manifest` y de la tarea `/rl HIGHEST`.
- **Riesgo de proceso:**
  - 0.20.7–0.20.16 se publicaron sin confirmación explícita.
  - El «Latest» llegó a apuntar a v0.20.1 (corregido).
  - Una prueba de elevación creó y borró una tarea programada temporal en el PC de desarrollo.
- **Este PC:** Windows Update pausado hasta 2038 por otro programa. Vigía lo avisa y no lo cambia.
