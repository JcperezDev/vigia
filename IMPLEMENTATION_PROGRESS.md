# Vigía — progreso de implementación

Actualizado 2026-10-30, rama `debloat-v2` (sin push ni merge). Una línea por requisito.
Estados: ✅ hecho y probado · 🟡 hecho, falta validación física/VM · ⛔ bloqueado (decisión tuya) · ⬜ pendiente.

Pruebas de referencia ahora (suite `s27` sobre `aa0bb59`, mismo código de app que el HEAD final, 2026-10-30 17:53): selftest **362 OK / 0 FAIL** · Puente **76/0** · `release.sh` **31/0** · herramientas de firma **39/0** · `preparar-relevo comprobar` rc 0 ·
contrato de sensores OK · traducciones pt/fr/de **2435 claves, 0 errores** · interfaz Apps **44/0** · interfaz Monitor **71/0** · LabGuard en el PC **24/0** · guarda del laboratorio **10/0** · `--sensor-service` en la compilación pública rc 2 · material de clave privada en logs **0** · VigiaLab **39** pruebas reales (`VM_VALIDATION_REPORT.md`). Estado: **NOT READY · `SIGNING_BLOCKED`**. Las cifras de más abajo son históricas.

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


## Matriz única de requisitos — cierre del 30-10-2026

Fuentes: Prompt 4 (Program Files y ayudante), Prompt 5 (firma, sensores, validación) y `/goal` del 30-10 (secciones 3–23). Estados: los del `/goal`. «Commit» es el último que tocó el requisito. Las pruebas citadas son de `s26` (`9459165`) o de las filas VM indicadas.

| ID | Requisito | Estado | Archivo / commit | Evidencia | Plataforma | Bloqueo / acción restante |
|---|---|---|---|---|---|---|
| R01 | Vigía abre como usuario (`asInvoker`); UAC solo al pedir algo administrativo | VM_TESTED | `app.manifest`, `Elevation.cs` · `3a59be2` | VM filas 22–28, 33 | VM 26300 | Validación física con tu UAC: NEEDS_USER_APPROVAL |
| R02 | Ayudante con lista cerrada; rechazos 2–7 | VM_TESTED | `AdminOps.cs`, `Elevation.cs` · `acd5ef0` | selftest 361/0; VM fila 34 (rc 3 como `estandar`) | VM | — |
| R03 | Ayudante se cierra a los 10 min sin pedidos; lo automático no cuenta (3A) | VM_TESTED | `Elevation.cs` (`IdleLimit`, `Background`) | Windows real 0.21.4 (10 min 2 s); VM fila 33 (`idleS 600`, 251 lecturas) | VM | — |
| R04 | No cortar una tarea administrativa de más de 10 min (sección 5) | VM_TESTED | `AdminOps.cs` `labHold`, `LabBrokerHold.cs` · `50ef8fb` | VM fila 33: 660 s, 23 latidos elevados, cierre 600 s tras el fin | VM | Límite: tarea de prueba, no operación de producto |
| R05 | Volver a pedir el ayudante; «No» de UAC no deja procesos | VM_TESTED | `Elevation.cs` | VM filas 28, 33 | VM | — |
| R06 | UAC con usuario estándar (sección 6) | VM_TESTED (parcial) | — | VM fila 34 | VM | Aviso con contraseña de admin desde `estandar`: BLOCKED (credencial autorizada) |
| R07 | Temperatura no disponible: sin 0 °C, negativos, ≥150 ni dato viejo (3B) | UNIT_TESTED + VM_TESTED | `app.js` `realTemp`, `Telemetry.cs` · `3aec484` | Monitor 71/0 (inversa 45 FAIL); VM fila 32 | VM | Lectura real en Ryzen: HARDWARE_VALIDATION_PENDING |
| R08 | Notas, aviso, README y PRODUCT sin promesas de temperatura (sección 8) | DONE | `changelog.json`, `i18n/*`, `README*.md`, `PRODUCT.md` · `b7519e6`, `9459165` | i18n 0 errores; contrato OK | — | Tu visto bueno al texto: NEEDS_USER_APPROVAL |
| R09 | Servicio SYSTEM fuera de 0.21.0; Vigía funciona sin él (3C) | VM_TESTED | `Vigia.csproj`, `Puente.csproj`, `Setup.cs` `RemoveSensors` · `3629a2c` | `--sensor-service` rc 2; zip sin servicio; VM filas 29, 32 | VM | — |
| R10 | Revisión externa del servicio | EXTERNAL_AUDIT_PENDING | `SENSOR_SERVICE_EXTERNAL_REVIEW.md` | encargo preparado | — | Revisor independiente |
| R11 | Clave de firma custodiada fuera del agente (P0) | BLOCKED → NEEDS_USER_APPROVAL | `SECURITY_KEY_REVIEW.md` · `68839d2` | metadatos y ACL sin abrir la clave; `--key-check` rc 3 | PC | Retirar `CodexSandboxUsers`, custodia offline, rotación con relevo: tu orden |
| R12 | Relevo de claves (multi-clave) sin cortar instalaciones viejas | UNIT_TESTED | `Puente/Program.cs` `NextKeys`, `Updates.cs` | Puente 76/0 con claves de prueba | — | Activar con clave nueva: NEEDS_USER_APPROVAL |
| R13 | Program Files con ACL solo SYSTEM/Admin; datos del usuario conservados | VM_TESTED | `Setup.cs`, `InstallTrust` | VM; `acl-test` 7/7 | VM | Migración real en tu PC: NEEDS_USER_APPROVAL |
| R14 | Puente: firmas, hashes, downgrade, reparación, rollback, candado, staging | UNIT_TESTED + VM_TESTED | `app/Puente/*` · `dd36490` | Puente 76/0; `zip-test`; VM downgrade rc 1, DLL dañada rc 0, archivo bloqueado rc 1, rollback rc 0, 0.21.9 sin firma válida rechazado | VM | — |
| R15 | Candidato 0.21.0 local sin firmar | IMPLEMENTED | `out/0.21.0` desde `9459165` | selftest empaquetado 361/0; hashes en `RELEASE_CANDIDATE_0.21.0.md` | — | Firmar e instalar el paquete firmado en VM: NEEDS_USER_APPROVAL |
| R16 | Debloat V2 (preview, quitar, historial, recuperar) | VM_TESTED | `Debloat.cs`, `AppPlan.cs` · `6014411` | Apps 44/0; VM (quitadas=1 fallos=0; recuperado) | VM | Win32 y aprovisionadas multiusuario solo vista previa; no se quitan sin tu orden |
| R17 | Privacidad: seis ajustes con fuente Microsoft Learn, fuera de «Aplicar todo» y del modo simple | VM_TESTED | `Tweaks.cs`, `tweaks.json` · `9f83671` | VM aplicar/revertir | VM | Aprobar ajustes: NEEDS_USER_APPROVAL; no se añadieron más (sin fuente nueva) |
| R18 | LabGuard en el backend; `LAB_ALLOW_GAME` solo quita la pausa | UNIT_TESTED + VM_TESTED | `LabGuard.cs` · `c260497` | LabGuard PC 24/0; guarda 10/0; en la VM el ayudante rechazó `labHold` sin flag | PC + VM | — |
| R19 | Órdenes de consola incompletas no abren la app | UNIT_TESTED | `Program.cs` `ConsoleVerbs` · `9db877f` | LabGuard PC 24/0 | PC | — |
| R20 | FPS fuera del ayudante (sin botón) | UNIT_TESTED | `AdminOps.cs` · `acd5ef0` | selftest (2 casos) | — | — |
| R21 | Perfiles de juego | REJECTED (0.21.0) | `GAME_PROFILE_RESEARCH.md` | Valorant y Marvel Rivals REJECTED; CS2 y Rocket League candidatos sin archivo real | — | Necesita instalación de prueba y tu permiso |
| R22 | BIOS solo lectura; ninguna escritura de firmware | UNIT_TESTED | `BiosWatch.cs` · `ed66d5c` | selftest (foto rota/manipulada) | — | Escritura: BLOCKED hasta auditoría y recuperación física |
| R23 | Cinco idiomas completos | UNIT_TESTED | `i18n/*` | 2435 claves, 0 errores | — | — |
| R24 | Sin secretos en logs ni en el paquete | UNIT_TESTED | `suite.sh` | 0 coincidencias; zip sin claves | — | — |
| R25 | Ventana negra tras `--tray` | VM_TESTED | `Tray.cs` · `14b506d` | VM tras reinicio | VM | — |
| R26 | Compatibilidad Win10/11 Home/Pro, AMD/Intel/NVIDIA, portátiles | BLOCKED | — | solo VM Enterprise 26300 | — | Hardware no disponible: HARDWARE_VALIDATION_PENDING |
| R27 | Mejoras de FPS medidas | REJECTED | — | sin benchmarks válidos; no se afirma nada | — | — |
| R28 | «Fable» | DONE (no aplica) | — | es de JITA, no de Vigía | — | — |
| R29 | Host sin cambios no autorizados | DONE | — | 0.20.17 pid 17212 intacto; sin servicio `VigiaSensores`; VM guardada | PC | Hyper-V sigue `Auto` (autorizado; revertir es tu decisión) |
| R30 | `labHold`/`--broker-hold` en la compilación pública | IMPLEMENTED | `AdminOps.cs`, `Program.cs` | doble guarda (interfaz rc 3 y ayudante rechaza) | PC + VM | Decisión: se quedan compilados porque la guarda está en el backend y está probada; sacarlos con `#if` sería otro binario sin probar en VM |

## Cierre 30-10 (tarde) — `7202134` / `d8e24d9`
- Firma: herramientas de aislamiento, firma fuera de este PC y relevo, probadas solo con claves de prueba (36/0). En `release.sh` y el selftest, un control de claves de confianza que deben coincidir. La clave real está intacta. **SIGNING_BLOCKED → NEEDS_OWNER_APPROVAL.**
- VigiaLab, filas 35–37: migración desde 0.20.17, Puente (firmas, corte, downgrade, reparación, rollback), Monitor sin servicio, usuario estándar. **VM_VALIDATED.** El aviso UAC con contraseña de administrador queda en NEEDS_OWNER_APPROVAL.
- Sensores: paquete para la revisión externa (**EXTERNAL_AUDIT_PENDING**) y procedimiento para el Ryzen (**HARDWARE_VALIDATION_PENDING**).
- Decisiones preparadas en `OWNER_DECISIONS_0.21.0.md`: texto de temperatura, seis ajustes de privacidad, UAC del PC, Hyper-V, migración y firma.
- Candidato `out/0.21.0` sin firmar sobre `d8e24d9`: hashes en `RELEASE_CANDIDATE_0.21.0.md`.

## Cierre 30-10 (noche) — `78f3fdf` / `aa0bb59` / `c8f0921`
| Requisito del `/goal` | Estado | Dónde / nota |
|---|---|---|
| Quitar `CodexSandboxUsers` de `.vigia` con copia, verificación y vuelta atrás | ✅ aplicado (autorizado) | `SECURITY_KEY_REVIEW.md`; `--key-check` rc 0; clave sin abrir |
| La corrección no prueba que la clave no se expusiera | ⛔ `SIGNING_BLOCKED` | rotar o mover: tu decisión (`OWNER_DECISIONS` §6) |
| Firma offline con identidad aislada | 🟡 herramientas probadas (39/0); el equipo de firma aparte lo pones tú | `firmar-fuera.ps1` |
| Transición de confianza sin cortar 0.20.17 | ✅ VM fila 38 con claves de laboratorio; `78f3fdf` arregla la subida de Puente | clave real: NEEDS_OWNER_APPROVAL |
| Notas de 0.21.0 iguales al comportamiento real | ✅ `aa0bb59` + contraste con el código | `RELEASE_CANDIDATE_0.21.0.md` |
| VigiaLab: instalación limpia, actualización firmada (laboratorio), firmas malas, downgrade, reparación, rollback, sin elevación, permisos, sin SYSTEM | ✅ filas 35–38 | con el paquete firmado de producción: repetir antes de publicar |
| VigiaLab: UAC con usuario estándar | 🟡 todo menos teclear la contraseña de admin | NEEDS_OWNER_APPROVAL |
| Temperaturas en tu Ryzen sin instalar servicios | 🟡 procedimiento listo | HARDWARE_VALIDATION_PENDING (no bloquea 0.21.0) |
| Servicio de sensores fuera hasta auditoría externa | ✅ fuera del paquete (rc 2) | EXTERNAL_AUDIT_PENDING |
| Reactivar UAC (estado, consecuencias, compatibilidad, reinicio, recuperación) | 🟡 preparado y probado en VM, no aplicado | `tools/host/uac.ps1`, `OWNER_DECISIONS` §3 |
| Permisos peligrosos de `AppData`/`Temp` y del perfil | 🟡 documentados, corrección limitada propuesta | `SECURITY_KEY_REVIEW.md` «Permisos fuera de .vigia» |
| Candidato 0.21.0 (archivos, SHA-256, manifiesto, changelog, Puente, cinco idiomas, sin drivers, servicio ni claves) | ✅ sin firmar | `RELEASE_CANDIDATE_0.21.0.md` |
| Hyper-V | sin tocar (`Auto`) | NEEDS_OWNER_APPROVAL para revertir |

## Ubicación y relevo desde 0.20.17 — 30-10 (noche) — `1ea871f` / `fea4ae0`

- **Aviso «Location has been turned off»:** causa = Sugerencias de red de 0.20.17 (`WlanQueryInterface` con
  current_connection y `WlanGetNetworkBssList`), lanzadas solas a los 20 s de abrir. Arreglo y pruebas en
  `LOCATION_PRIVACY_FIX.md`. Pendiente: confirmar en un equipo con Wi‑Fi conectado (este PC no tiene ninguno).
- **Puente:** se sustituye solo si la versión del manifiesto es mayor (Setup.cs 466); el SHA‑256 se comprueba en
  lo descargado. `release.sh` ya no deja publicar un Puente cambiado con el mismo número.
- **Relevo desde 0.20.17** en VigiaLab con claves de laboratorio: fila 40 de `VM_VALIDATION_REPORT.md`.
- Pruebas sobre `fea4ae0`: selftest 371/0, location-test 25/0, Puente 76/0, release 31/0, firma 39/0, i18n 2435 claves.
- Sigue **NOT READY · SIGNING_BLOCKED** (clave real sin custodia resuelta).

## Últimas validaciones antes de publicar — 30-10 (noche, 2.ª) — `6ea6126` / `6f8e5ff`

- **Puente nuevo + zip malo (hallazgo confirmado):** `InstallFlow` cambiaba Puente antes de bajar y comprobar el zip.
  Reproducido en VigiaLab con el código de `fea4ae0` (fila 41): Vigía 0.21.2 se quedó con Puente 2.0.3 (solo clave B)
  sin instalar nada. **Arreglo `6ea6126`:** el paquete se comprueba antes (tamaño, SHA‑256, firma); el Puente nuevo
  tiene que aceptar esta misma publicación (`--verify-manifest`) antes de entrar; si el cambio falla vuelve el anterior
  y un corte entre los dos renombres se recupera al arrancar. El Puente relanzado no vuelve a bajar el zip.
  `puente-test` **85/0** (11 casos nuevos; con el Puente de `ab35542` fallan 3). Rollback y reparación siguen pasando.
- **Candidato atado al código (`6f8e5ff`):** `candidato.txt` (commit + huellas); `VIGIA_RELEASE_SIGNED=1` se niega si
  `app/` cambió o si lo firmado no es lo compilado. `tools/candidate-check.js`. `release-test` **33/0**, `signing-test` **39/0**.
- **Candidato 0.21.0 rehecho sobre `6f8e5ff`, sin firmar:** zip `6254ed47…836c`, Puente 2.0.1 `cb68d8b7…a257`,
  manifiesto `dd165736…e1e5`; selftest 371/0; `candidate-check` 19/0. Puente 2.0.1 > 1.4.0 (el de `v0.20.17`): coherente.
- **Relevo:** `rot2` (A→B, 7 pasos) y `directo2`/`escalon` revisados: correctos salvo el caso de la fila 41.
  Repetido en VigiaLab con el arreglo (`directo` y `escalon` desde 0.20.17, paquetes desde `6f8e5ff`): zip alterado
  → rc 1 y Puente sigue en 2.0.2; transición, promoción, rollback, downgrade y datos correctos (fila 41).
- **Wi‑Fi conectado:** sin equipo (3 adaptadores Wi‑Fi, todos desconectados) → HARDWARE_VALIDATION_PENDING.
- **Firma:** clave real no abierta, copiada ni rotada; ACL = SYSTEM, Administradores y tu usuario; sin cifrar;
  `CodexSandboxOffline/Online` siguen habilitadas. **SIGNING_BLOCKED.**
