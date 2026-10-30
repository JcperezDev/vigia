# Vigía — progreso de implementación

Actualizado 2026-10-30, rama `debloat-v2` (sin push ni merge). Una línea por requisito.
Estados: ✅ hecho y probado · 🟡 hecho, falta validación física/VM · ⛔ bloqueado (decisión tuya) · ⬜ pendiente.

Pruebas de referencia ahora (suite `s25` sobre `c260497`, 2026-10-30 00:27–00:28): selftest **358 OK / 0 FAIL** · Puente **76/0** · `release.sh` **31/0** ·
contrato de sensores OK · traducciones pt/fr/de **2435 claves, 0 errores** · interfaz Apps **44/0** · interfaz Monitor **71/0** · LabGuard en el PC **22/0** · guarda del laboratorio **10/0** · `--sensor-service` en la compilación pública rc 2 · material de clave privada en logs **0** · VigiaLab **32** pruebas reales (`VM_VALIDATION_REPORT.md`). Cifras anteriores de este archivo son históricas.

## Validación real — 29 de octubre

> Histórico de la mañana. Después se activó Hyper-V (autorizado) y la VM sí corrió: ver «Cierre 2026-10-29 (VigiaLab)» al final y `VM_VALIDATION_REPORT.md` (28 pruebas VM_VALIDATED). `HARDWARE_TESTED` sigue sin nada nuevo.

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
| Claves de privacidad nuevas | ⏸ decisión | no se añadió ninguna: serían funciones nuevas y van como propuesta antes de implementar |
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
| P0 Vigía como usuario + ayudante bajo demanda con lista cerrada | ✅ VM_VALIDATED | `Elevation.cs`, `AdminOps.cs`; UAC real en la consola de VigiaLab (Sí/No; se cierra solo a los 10 min sin pedidos); en VigiaLab: `asInvoker`, Run sin privilegios, 8/8 rechazos del ayudante (argumentos, otra imagen, otra cuenta, tubería ajena, carpeta escribible, usuario estándar) |
| P0 Clasificar todas las operaciones (41 admin / 48 usuario) | ✅ | `docs/PRIVILEGES.md` |
| P0 Lecturas sin permisos no inventan resultados | ✅ | Diagnóstico, Seguridad, Salud, Limpieza |
| P0 Copias como dato no confiable (esquema, destinos, HMAC) | ✅ | `BackupGuard.cs` |
| P1 Instalación en `%ProgramFiles%\Vigia` con ACL mínimas | ✅ VM_VALIDATED | `app/Puente/Layout.cs`; ACL leídas en VigiaLab y cuenta `estandar` real |
| P1 Migrador desde `%LOCALAPPDATA%\Programs` conservando datos | ✅ VM_VALIDATED | `app/Puente/Setup.cs`; 0.20.17 → 0.21.0 en VigiaLab con datos conservados |
| P1 Anti-downgrade (app y Puente) con piso de seguridad | ✅ | `estado.json`, `EnsurePuenteAsync` |
| P1 TOCTOU cerrado | ✅ | `.descargas` solo-admin; firma en memoria en Vigía |
| P1 Recuperación (diario, `--repair`, `--rollback`) | 🟡 | proceso matado y archivo bloqueado probados en VM; falta apagado real a mitad |
| P1 Desinstalación limpia que deja tus datos | ✅ VM_VALIDATED | VigiaLab, tras `048de17` |
| P2 `release.sh` borrador → verificar → publicar | ✅ | probado contra mock; nunca contra GitHub real; cada archivo del borrador se compara byte a byte, no solo por tamaño |
| Revisión extra del instalador (programas de Windows con ruta fija, un Puente a la vez, reparar sin perder la versión buena, mínima segura guardada aunque falle) | ✅ | `Tweaks.Sys`/`Native.Sys`; `Acl.Lock` en instalar/revertir/desinstalar; Puente 64/0 |
| Copias de juegos: solo el archivo de Fortnite se repone | ✅ | `BackupGuard.FileTarget`; selftest 302/0 |
| P2 Clave de firma protegida | ⛔ `SIGNING_BLOCKED` | legible por `CodexSandboxUsers` y sin cifrar; sin evidencia de lectura; necesita tu OK (`SECURITY_KEY_REVIEW.md`) |
| P2 Relevo de claves (confiar en la actual y en la siguiente) | ✅ | `c160702`; lista vacía hoy; probado con claves de prueba |
| P2 Procedimiento de rotación | ✅ | documentado, no ejecutado |
| P3 Debloat bajo la arquitectura nueva | ✅ VM_VALIDATED | quitar, recuperar, aprovisionamiento y casos negativos en VigiaLab |
| P3 Cinco idiomas | ✅ | 0 errores |
| Publicar 0.21.0 / merge / push | ⛔ | necesita tu autorización |

## Lo siguiente que se puede hacer sin autorización

1. Perfiles de juegos y claves de privacidad nuevas (proponer lista antes de implementar).

## Lo que necesita VM o tu decisión

Validación en VM (`docs/INSTALL.md`), clave de firma, migración real, publicación, borrar restos de `%TEMP%`.

## Sensores de la CPU (29 de octubre)

- `3e55d0f`: PawnIO lo prepara Puente al instalar/actualizar/Reparar (nunca Monitor, nunca con juego abierto).
- `054c1be`: servicio de solo lectura «Sensores de Vigía»; sin permisos, Vigía lee los grados desde el servicio. Probado con publicador de prueba y token de usuario básico (77,25 °C en Ryzen 9 9900X). Servicio real sin registrar en este PC: pendiente VM y el Ryzen del amigo.
- Selftest 329/0, Puente 74/0, release 31/0, traducciones 0 errores. Detalle: `CPU_SENSOR_INTEGRATION_REPORT.md`.

## Fase 7 BIOS/UEFI — comparación offline (29 de octubre)

| Tarea | Estado | Archivos | Prueba | Resultado | Riesgo / siguiente |
|---|---|---|---|---|---|
| Comparar BIOS con la última lectura (versión; RAM, ReBAR, UEFI, Secure Boot, TPM que pasan de bien a mal) | `UNIT_TESTED` + captura real | `app/Vigia/BiosWatch.cs`, `Bios.cs`, `Program.cs`, `tools/i18n-add-bios.js`, i18n | selftest st71 «BIOS: comparación» 10/10; `--snap bios@es/en` con foto anterior de prueba en carpeta de datos aparte | avisa «La BIOS cambió» y «Memoria RAM: estaba bien y ahora no»; foto rota o manipulada = se ignora | traducción en pantalla pendiente de recompilar (textos añadidos, `i18n-check` 0 errores) |
| Exclusión de escritura de firmware en la build | verificado (búsqueda en fuente y DLL) | — | grep fuente + binarios | 0 APIs de escritura, 0 drivers AMI | — |
| Privacidad: aviso traducido y «Aplicado» frente al registro | `VM_VALIDATED` | `PRIVACY_TWEAK_REVIEW.md` | `--tweaks-lab apply/revert` en VigiaLab + lectura del registro | los seis se aplican y el registro vuelve idéntico al revertir | efecto visible en escritorio, Home real y dominio real: sin validar |

Dato real de solo lectura de este PC: la RAM va a 5600 MT/s siendo de 6000 → EXPO apagado (no se cambió nada).

## Cierre 2026-10-29 (VigiaLab)
- ✅ Migración 0.20.17 → 0.21.0, ACL de Program Files, usuario estándar, actualización, downgrade, rollback, reparación (también con archivo bloqueado), dos Puentes, desinstalación: probado en VM.
- ✅ Servicio de sensores en VM: arranque como SYSTEM, parar/arrancar, relanzado tras caída, sin PawnIO, suplantación del mapa rechazada. Temperaturas: 🟡 solo en hardware físico.
- ✅ Debloat V2 en VM: quitar, historial, recuperar, aprovisionamiento en Avanzado, casos negativos.
- ✅ UAC en VM (consola): aviso real, No/Sí, cierre por inactividad del ayudante (`97b9c29`), ventana en negro tras arrancar con Windows (`14b506d`). Suite s17: 352/0, 76/0, 31/0.
- ⛔ clave de firma (`SIGNING_BLOCKED`, permisos y rotación tuyos) · revisión externa del servicio SYSTEM · migración real en tu PC, publicar, merge, push.
- «Fable» = JITA, no Vigía. Detalle y siguiente paso: `FINAL_VALIDATION_REPORT.md`.


## Cierre de seguridad 0.21.0 (29–30 de octubre)
- ✅ Ayudante: el plazo de 10 min cuenta desde que termina el último pedido tuyo; nunca corta una tarea en marcha (selftest, simulado); no pierde medio mensaje al vencer el plazo (selftest); las lecturas automáticas no lo renuevan y, si está cerrado, no lo reabren ni piden UAC. VM 0.21.7: cierre ≈10 min con Monitor abierto, sin UAC espontáneo, nueva solicitud con UAC (fila 31). Regla escrita en `docs/PRIVILEGES.md`.
- ✅ «Temperatura de CPU no disponible.» con motivo (`CpuTempWhy`), sin «°C» ni 0 °C; cinco idiomas; VM fila 30.
- ✅ Servicio de sensores **fuera de la versión pública** (`SensorService`/`SENSOR_SERVICE`, `release.sh` aborta si se cuela, Puente 2.0.1 lo quita al actualizar; VM fila 29). Encargo externo: `SENSOR_SERVICE_EXTERNAL_REVIEW.md` (⛔ EXTERNAL_AUDIT_PENDING).
- ✅ `LAB_ALLOW_GAME=1`: 0 usos en `app/` y `tools/`; en los scripts de laboratorio solo salta la pausa por juego. Identidad de VigiaLab siempre comprobada; prueba de regresión `scratch/lab/test/guard-test.ps1` 10/0 (la versión anterior da 10 FAIL). LabGuard en el PC: `tools/labguard-host-test.js` **22/0** con `VIGIA_LAB=1` y `LAB_ALLOW_GAME=1` (`--lab`, `--tweaks-lab`, `--apps-run`, `--apps-restore` rc 3 sin escribir nada; órdenes incompletas rc 2 sin abrir la interfaz; el Run del usuario sin cambios).
- ✅ Clave: metadatos y ACL sin abrirla (sin cambios desde su creación); procedimiento exacto A/B/C en `SECURITY_KEY_REVIEW.md`. ⛔ `SIGNING_BLOCKED` hasta que lo autorices.
- ✅ Versiones: público v0.20.17; 0.21.0 sigue siendo la siguiente válida (0.21.1–0.21.8 solo en VigiaLab con clave ficticia). Puente 2.0.1. Candidato local sin firmar en `out/0.21.0` (`RELEASE_CANDIDATE_0.21.0.md`).
- 🟡 UAC con usuario estándar y temperaturas en hardware físico: pendientes (HARDWARE_VALIDATION_PENDING / NEEDS_OWNER_APPROVAL).

- ✅ 30-10: en la versión pública Puente quita el servicio de laboratorio **antes** del paso de PawnIO, aunque este falle o tarde (`3aec484`; VM fila 32). El Monitor trata 0 °C, negativos y 150 °C o más como «sin dato» (`3aec484`; `tools/ui-monitor-test.js` 71/0 en cinco idiomas; con el `app.js` anterior da 45 FAIL).
- ✅ 30-10: LabGuard también para lo que el laboratorio relaja en Apps (riesgo medio), `--autostart` y `--msdl-probe`; una orden de consola incompleta o solo de laboratorio sale con rc 2 en vez de abrir Vigía (`9db877f`). Antes, `--apps-run` incompleto arrancaba la app normal.
- ✅ 30-10: la cifra «359» de la suite contaba una línea informativa en minúscula; son **358** pruebas. Conteo de la suite corregido.
- 🟡 Nota de la 0.21.0 «los grados del procesador se ven mientras el ayudante está abierto»: es lo que hace el código, sin validar en hardware físico (HARDWARE_VALIDATION_PENDING). Validarla o quitar esa frase antes de publicar.
