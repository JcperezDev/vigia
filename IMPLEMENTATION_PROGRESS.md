# Vigía — progreso de implementación

Actualizado 2026-10-29, rama `debloat-v2` (sin push ni merge). Una línea por requisito.
Estados: ✅ hecho y probado · 🟡 hecho, falta validación física/VM · ⛔ bloqueado (decisión tuya) · ⬜ pendiente.

Pruebas de referencia ahora: selftest **318/0** · UI de Apps **44/0** · traducciones **0 errores** · Puente **74/0** ·
`release.sh` **31/0**.

## Validación real — 29 de octubre

- `IMPLEMENTED` + `UNIT_TESTED`: firma separada (`fe5f5f2`); privacidad opcional (`9f83671`); candado de comandos de laboratorio (`ffe469e`).
- Clave real: `--key-check` recompilado devuelve **3**, confirma lectura heredada para `CodexSandboxUsers`. No se modificó ni rotó. Posible exposición, no evidencia de lectura.
- `BLOCKED`: Hyper-V no arranca: `hypervisorlaunchtype Off`, `HypervisorPresent=False`. No se cambia el arranque del anfitrión.
- Se restauró el checkpoint `limpia` de VigiaLab; el intento de arranque falló y la VM quedó `Saved`. No se ejecutaron pruebas dentro del invitado.
- Artefactos de laboratorio y 0.20.17 preparados en scratch; firmas de ZIP y Puente históricos verificadas (rc=0). No se ejecutó el instalador histórico en el host.
- `VM_TESTED` y `HARDWARE_TESTED`: ninguna validación nueva completada. Instalación, migración, UAC y Debloat reales pendientes.
- Estado de publicación: **NOT READY**. Sin push, merge, release ni migración de la instalación principal.

## Prompt 1 — auditoría de ajustes, rendimiento y seguridad (hasta 0.20.17)

| Requisito | Estado | Dónde / nota |
|---|---|---|
| Inventario de ajustes con estado, fuente y restauración | ✅ | `tweaks.json`, `docs/TWEAKS.md`, `tools/tweaks-review.js` |
| Ajustes de riesgo medio/alto retirados | ✅ | `Tweaks.RetiredPending()` lista los que quedaron aplicados y `undoRetired` los deshace |
| Rollback si un ajuste falla a mitad | ✅ | `Tweaks.Apply` / `RevertOp` |
| Catálogo dentro del `.exe` | ✅ | recurso embebido |
| PawnIO solo con «Sí» en el Monitor | ✅ | `Pawn.cs`, `Program.cs` |
| Frametimes (ETW DxgKrnl/D3D9) | 🟡 | falta validar en juegos reales y en GPU AMD/Intel |
| ReBAR por el método del fabricante | 🟡 | sin cambio; validación física |
| Extracción segura de ZIP, firma ECDSA | ✅ | `SafeExtract`, `--zip-test` |
| QA visual de Ajustes (`--snap`) en/es | ✅ | captura fuera de pantalla, sin ventana ni foco; en/es sin desbordes |

## Prompt 2 — segunda auditoría (Toolbox, Startup, memoria, Ayuda)

| Requisito | Estado | Dónde / nota |
|---|---|---|
| Herramientas con paquete WinGet verificado, editor y procedencia | 🟡 | `Toolbox.cs`, `docs/TOOLS.md`; 9 IDs confirmados con `winget show`; falta UAC real |
| Startup sin entradas personales, servicios validados | ✅ | `Startup.cs` (`Sc`, `ScModes`, `ServiceExists`) |
| Memoria: opción segura por defecto | ✅ | `MemOptions.Safe` |
| Centro de seguridad de solo lectura | ✅ | `Security.cs` |
| Limpieza sin vaciar la Papelera | ✅ | `Cleanup.cs` |
| Optimizar unidades | ✅ | `Optimize.cs` |
| Ayuda reescrita | ✅ | `wwwroot` |
| CI sin claves ni tokens | ✅ | `.github/workflows/ci.yml` |
| Perfiles de juegos nuevos | ⬜ | no empezado |
| Claves de privacidad nuevas | ⬜ | no empezado |
| Compatibilidad AMD / Intel probada | 🟡 | solo hay hardware AMD CPU + NVIDIA |

## Prompt 3 — Debloat V2 (`DEBLOAT_IMPLEMENTATION_REPORT.md`)

| Requisito | Estado | Dónde / nota |
|---|---|---|
| Inventario AppX (usuario, todos, aprovisionadas) y Win32 | ✅ | `AppInventory.cs`; sin permisos cae al usuario actual y lo marca |
| Catálogo con categorías y protecciones (frameworks, Store, dependencias) | ✅ | `apps.json` v2 (61 apps), `AppCatalog.cs` |
| Útiles nunca marcadas por defecto | ✅ | flag `useful` |
| Plan con vista previa y token (si cambia, no ejecuta) | ✅ | `AppPlan.cs` |
| Historial, instantánea y recuperación A–D etiquetada | 🟡 | `AppHistory.cs`; recuperación real sin probar |
| Exportación JSON/CSV sin rutas ni SID | ✅ | `Documentos\Vigía` |
| Modo simple sin quitar apps Xbox | ✅ | `Simple.cs` |
| Win32 sin `UninstallString` automático | ✅ | flujo manual |
| UI de Apps (modos, filtros, buscador, doble confirmación) | 🟡 | 44/0 en Chrome con WebView simulado; falta la ventana real |
| Desinstalación real, aprovisionadas, Win10/Home/Enterprise, varios usuarios | ⬜ | solo en VM con snapshot |
| IDs de Microsoft sin fuente oficial | ⛔ | 7 candidatos no añadidos hasta tener documento de Microsoft |

## Prompt 4 — seguridad, instalación y actualización (`SECURITY_MIGRATION_REPORT.md`)

| Requisito | Estado | Dónde / nota |
|---|---|---|
| P0 Inicio sin tarea elevada (Run de HKCU) | ✅ | `AutoStart.cs`; Puente quita la tarea vieja solo si es de Vigía |
| P0 Vigía como usuario + ayudante bajo demanda con lista cerrada | 🟡 | `Elevation.cs`, `AdminOps.cs`; falta UAC real en VM |
| P0 Clasificar todas las operaciones (41 admin / 48 usuario) | ✅ | `docs/PRIVILEGES.md` |
| P0 Lecturas sin permisos no inventan resultados | ✅ | Diagnóstico, Seguridad, Salud, Limpieza |
| P0 Copias como dato no confiable (esquema, destinos, HMAC) | ✅ | `BackupGuard.cs` |
| P1 Instalación en `%ProgramFiles%\Vigia` con ACL mínimas | 🟡 | `app/Puente/Layout.cs`; probado con usuario simulado, falta cuenta estándar real |
| P1 Migrador desde `%LOCALAPPDATA%\Programs` conservando datos | 🟡 | `app/Puente/Setup.cs`; probado en `%TEMP%`, falta 0.20.17 real en VM |
| P1 Anti-downgrade (app y Puente) con piso de seguridad | ✅ | `estado.json`, `EnsurePuenteAsync` |
| P1 TOCTOU cerrado | ✅ | `.descargas` solo-admin; firma en memoria en Vigía |
| P1 Recuperación (diario, `--repair`, `--rollback`) | 🟡 | probado con cortes simulados; falta apagado real |
| P1 Desinstalación limpia que deja tus datos | 🟡 | probado en `%TEMP%`; falta VM |
| P2 `release.sh` borrador → verificar → publicar | ✅ | probado contra mock; nunca contra GitHub real; cada archivo del borrador se compara byte a byte, no solo por tamaño |
| Revisión extra del instalador (programas de Windows con ruta fija, un Puente a la vez, reparar sin perder la versión buena, mínima segura guardada aunque falle) | ✅ | `Tweaks.Sys`/`Native.Sys`; `Acl.Lock` en instalar/revertir/desinstalar; Puente 64/0 |
| Copias de juegos: solo el archivo de Fortnite se repone | ✅ | `BackupGuard.FileTarget`; selftest 302/0 |
| P2 Clave de firma protegida | ⛔ | legible por `CodexSandboxUsers` y sin cifrar; necesita tu OK (`docs/SIGNING.md`) |
| P2 Procedimiento de rotación | ✅ | documentado, no ejecutado |
| P3 Debloat bajo la arquitectura nueva | 🟡 | `appsScan/appsPreview` sin permisos, `appsRun/Restore` con ayudante; falta VM |
| P3 Cinco idiomas | ✅ | 0 errores |
| Publicar 0.21.0 / merge / push | ⛔ | necesita tu autorización |

## Lo siguiente que se puede hacer sin autorización

1. Perfiles de juegos y claves de privacidad nuevas (proponer lista antes de implementar).

## Lo que necesita VM o tu decisión

Validación en VM (`docs/INSTALL.md`), clave de firma, migración real, publicación, borrar restos de `%TEMP%`.
