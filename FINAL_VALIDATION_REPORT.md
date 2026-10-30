# Vigía — informe final de validación (rama `debloat-v2`)
Fecha: 2026-10-30 (actualizado 18:00) · **Estado: NOT READY · `SIGNING_BLOCKED`** (motivos en «Bloqueos»). HEAD final `debloat-v2`: el commit de este informe, sobre `c8f0921`. Candidato local sin firmar sobre `c8f0921`: `RELEASE_CANDIDATE_0.21.0.md`.
Solo commits locales: sin push, merge ni release. La instalación principal del PC no se tocó.

## Cierre 30-10 (noche) — `/goal` «resolver bloqueos finales»
- **Clave (P0):** con tu autorización, `CodexSandboxUsers` ya no tiene acceso a `.vigia` (copia de permisos previa, metadatos iguales, tu acceso comprobado, `--key-check` rc 0, vuelta atrás con `-Deshacer`). La clave no se abrió ni copió. **Sigue `SIGNING_BLOCKED`:** pudo leerse antes, está sin cifrar y tu cuenta es administradora con UAC desactivado. Permisos peligrosos fuera de `.vigia` documentados con corrección limitada propuesta (no aplicada): `SECURITY_KEY_REVIEW.md`.
- `78f3fdf` **relevo:** `preparar-relevo.js` sube la versión de Puente; sin eso, tras rotar se cortaban las actualizaciones. Probado de punta a punta en VigiaLab con dos claves de laboratorio (fila 38).
- `aa0bb59` **notas:** los seis ajustes de privacidad entran en las notas de 0.21.0, cinco idiomas. Contrastado con el código: «Aplicar todo» (`twAll`) excluye todo lo que tiene aviso, los seis lo tienen (selftest) y el modo simple no propone nada con aviso.
- `c8f0921` **UAC:** `tools/host/uac.ps1` (ver, activar con copia, deshacer exacto). Probado en VigiaLab con tus valores simulados (fila 39). En tu PC solo se leyó: `EnableLUA=0`, `ConsentPromptBehaviorAdmin=0`, `PromptOnSecureDesktop=0`. **No se cambió.**
- Candidato 0.21.0 rehecho sobre `c8f0921` (hashes nuevos). Tu 0.20.17 sigue en `%LOCALAPPDATA%\Programs\Vigia` (0.20.17.0, abierta desde las 17:15), sin `Program Files\Vigia` ni servicio de sensores. Hyper-V sin tocar. VigiaLab guardada (`Saved`).

## Cierre 30-10 (tarde) — lo último que se podía hacer sin ti
- `7202134` **firma:** `tools/signing/quitar-acceso-sandbox.ps1` (deja `.vigia` solo para ti, SYSTEM y Administradores, corta la herencia y se puede deshacer), `firmar-fuera.ps1` (firma en un equipo aparte sin red; se niega si cambió Puente, si hay red o si la clave tiene permisos de más) y `preparar-relevo.js` (rotación escalonada `anadir` → `promover`). `release.sh` y `--selftest` cortan si Puente y Vigía no confían en las mismas claves. **Probado solo con claves de prueba: 36/0.** Sobre la clave real no se ejecutó nada.
- Hallazgo nuevo (solo permisos): `CodexSandboxUsers` también tiene lectura explícita en `AppData` y Modificar en `AppData\Local\Temp`. No se tocó porque afecta a otras herramientas.
- Pruebas afectadas por los cambios, sobre `7202134`: selftest **362/0** (+1: «una clave de relevo mal pegada se detecta»), `release-test` **31/0**, contrato de sensores OK y `preparar-relevo comprobar` OK. El resto de suites no cambió (`s26` sigue valiendo).
- VigiaLab (filas 35–37): migración desde 0.20.17, Puente con firmas malas, corte, downgrade, reparación, archivo abierto y rollback; Monitor sin servicio con «Temperatura de CPU no disponible.»; usuario estándar completo salvo la contraseña de administrador.
- Sensores: `SENSOR_SERVICE_EXTERNAL_REVIEW.md` con el paquete de entrega (commit, huellas, compilación, preguntas). La auditoría externa **no está hecha**. `SENSOR_RYZEN_VALIDATION.md` explica cómo validar tu Ryzen sin instalar nada. Esa prueba **no se ha hecho**.

## Commits
- Tanda del 30-10 (desde `558c662`): `50ef8fb` labHold y `--broker-hold` (solo VigiaLab) · `b7519e6` notas y aviso de temperatura sin promesas (5 idiomas) · `68839d2` revisión de la clave (metadatos de hoy, UAC del PC desactivado) · `acd5ef0` fuera `fpsStart`/`fpsCancel` del ayudante · `9459165` README y PRODUCT · más el commit de este informe.
- Tanda anterior: commit inicial `ed66d5c` (BIOS solo lectura).
- Creados: `791fdc9` sensores más robustos (los siete arreglos de la revisión) · `4dbe8c8` install-check sin `es.json` · `1894d15` migración de la tarea vieja y sesión 0 · `1af5284` servicio como SYSTEM · `61a102f` reparación con archivo abierto · `bf0a2f8` idiomas (85 mensajes) · `6014411` Apps por consola solo en VM · `03ad4c4` PawnIO al desinstalar · `048de17` carpeta vacía al desinstalar · `e0cde22` este informe · `17ad7e8` ayudante probado en VM y juegos evaluados · `81ac14c` privacidad aplicada y revertida en VM (`--tweaks-lab`) · `7ece97d` informe · `c160702` relevo de claves · `881d769` reauditoría de la clave (`SIGNING_BLOCKED`) · `cf20c0f` ayudante con cierre por inactividad y motivo visible al cancelar UAC · `04f5ffb` las lecturas de sensores ya no mantienen vivo el ayudante; `SENSOR_SERVICE_AUDIT.md` · `97b9c29` tampoco la memoria automática; aviso de temperatura; compatibilidad y anti-rollback de la firma · `14b506d` ventana en negro al abrir tras arrancar con Windows · `34d03df` UAC real en VM · `bfa46c2` plazo del ayudante desde el último pedido, servicio fuera de la versión pública, temperatura no disponible · `dd36490` Puente 2.0.1 · `3aec484` Puente quita el servicio antes de PawnIO; Monitor descarta 0 °C y fuera de rango; prueba del Monitor · `3629a2c` contrato · `9db877f` LabGuard en Apps/`--autostart`/`--msdl-probe` y órdenes incompletas sin abrir la app · `c260497` contrato de `LAB_ALLOW_GAME` · `c82336b` informes de clave y revisión externa.
- Archivos de código tocados: `app/Puente/Setup.cs`, `app/Puente/SensorService.cs`, `app/Puente/Program.cs`, `app/Vigia/SensorHost.cs`, `Elevation.cs`, `AutoStart.cs`, `Telemetry.cs`, `Updates.cs`, `Program.cs`, `wwwroot/i18n/*`, `tools/i18n-extract.js`, `tools/i18n-check.js`, `tools/i18n-add-mensajes.js`, `tools/sensor-install-contract-test.py`, `AdminOps.cs`, `Tray.cs`, `wwwroot/app.js`, `tools/puente-test.js`.

## Pruebas automáticas (código actual, compilado de cero)
Última tanda: suite `s27` sobre `aa0bb59` (2026-10-30 17:53–17:54), compilado de cero, sin cambios sin commit. `c8f0921` solo añade `tools/host/uac.ps1` (fuera de la app). Las cifras de `s26` y antes son históricas.

| Comando | Resultado |
|---|---|
| `dotnet build` Vigía / Puente / Puente-test (Release win-x64) | rc 0, 0 warnings, 0 errores |
| `Vigia.exe --selftest` con `VIGIA_DATA` aislado | **362 OK / 0 FAIL** |
| `node tools/puente-test.js` | **76 / 0** |
| `node tools/release-test.js` (GitHub simulado) | **31 / 0** |
| `node tools/signing/signing-test.js` (claves de prueba) | **39 / 0** (en la suite falló al arrancar: el `node` de git-bash pedía TTY; repetido con `node.exe` sobre el mismo HEAD) |
| `node tools/signing/preparar-relevo.js comprobar` | rc 0: 0 claves de relevo, iguales en Puente y Vigía (mismo arreglo que arriba) |
| `python -I tools/sensor-install-contract-test.py` | OK |
| `node tools/i18n-check.js` | pt/fr/de: 2435 claves, 0 errores, 0 distintas |
| `node tools/ui-apps-test.js` (Chrome sin ventana) | 44 / 0, sin errores de JavaScript |
| `node tools/ui-monitor-test.js` | 71 / 0 |
| `node tools/labguard-host-test.js` en este PC | 24 / 0 |
| `scratch/lab/test/guard-test.ps1` (guarda de los scripts de VM) | 10 / 0 |
| `Vigia.exe --sensor-service` en la compilación pública | rc 2 (no existe) |
| Logs de la suite con material de clave privada | 0 |
| Candidato `release.sh` (pasos 1–4) sobre `c8f0921` | rc 0, selftest del paquete 362/0, `zip-test` 7/7, `acl-test` bien, i18n del paquete 0 errores |

## Pruebas en VM (reales, no simuladas)
Detalle en `VM_VALIDATION_REPORT.md`. Las 28 de base se conservan; 29–32 cubren lo que cambió hasta 0.21.8; 33–37 son del 30-10 (tarea de 11 min, usuario estándar, migración desde 0.20.17, Puente/rollback, Monitor sin servicio) y **38–39 de esta noche** (relevo de claves A → B de punta a punta; herramienta de UAC).

## Seguridad del servicio «Sensores de Vigía»
**Fuera de la versión pública** hasta la revisión externa (`SENSOR_SERVICE_EXTERNAL_REVIEW.md`, EXTERNAL_AUDIT_PENDING). Lo de abajo es la revisión **interna** de la compilación de laboratorio; no es independiente.
- **¿Hace falta LocalSystem?** Sí. PawnIO solo atiende a administradores y SYSTEM; en este PC la lectura salió sin admin = nada y con admin = 62,63 °C. Alternativas de menos privilegio: LocalService o NetworkService no pueden abrir el driver; una cuenta virtual necesitaría estar en Administradores, que no es menos. Dejar a Vigía elevada todo el tiempo sería peor. Se queda LocalSystem, reducido al mínimo:
- **Superficie:** no hay tubería con nombre, puerto, archivo de entrada ni comandos. Solo escribe un bloque fijo de 64 bytes (`Global\VigiaSensores`, magia `VGS1`). Por eso la ACL de named pipes no aplica: no existen.
- **Permisos:** del servicio, el SDDL da a los usuarios solo consulta (no pueden parar, arrancar ni reconfigurar: comprobado como `estandar`). Del ejecutable, está en Program Files, escribible solo por SYSTEM/Administradores (comprobado), y el servicio se niega a arrancar si su carpeta es escribible por otros. Del mapa, los usuarios lo leen y no lo escriben (comprobado). Si otro se adelanta a crear el mapa, el servicio no publica y Vigía no lee datos falsos (comprobado).
- **Autenticación del cliente / argumentos:** el servicio no tiene clientes ni argumentos de entrada; Vigía comprueba que el dueño del mapa sea SYSTEM/Administradores antes de creerle.
- **Recuperación:** el SCM lo relanza a los 60 s si cae (comprobado). Sin PawnIO, sigue vivo sin publicar lecturas (comprobado).
- **Instalación y desinstalación:** solo Puente, con UAC; la desinstalación lo quita (comprobado). Integridad: Puente verifica SHA-256 por archivo antes de activar una versión.
- **Riesgo que queda:** un fallo en el propio código del servicio o en PawnIO corre con SYSTEM. Mitigación: código mínimo y sin entradas. **Es auditoría interna, no independiente.** No se instaló en tu PC principal (como pediste).

## Instalación y migración
Program Files con ACL correctas, datos en `%LOCALAPPDATA%\Vigia` conservados (hash igual), la tarea vieja con privilegios máximos se quita, el inicio queda como Run HKCU sin privilegios y no quedan componentes en la carpeta antigua. Todo comprobado en VM. En tu PC sigue la 0.20.17 con su tarea de privilegios máximos, que es lo esperado hasta migrar.

## Debloat V2
Probado en Windows real (VM): inventario AppX/Win32/aprovisionadas/dependencias, protecciones, vista previa, quitar, historial, recuperación por registro y por la Store, plan obsoleto, paquete inexistente, protegidas, dependencias y usuario estándar. La cancelación es la ausencia de ejecución tras la vista previa (sin plan no se quita nada; comprobado como rc 5/3). Sin probar: la reinstalación completa desde la Store.

## Privacidad · Juegos · BIOS
- Privacidad: seis directivas con fuente Microsoft Learn, fuera de «Aplicar todo» y del modo simple. **En VigiaLab se aplicaron y revirtieron de verdad**: las 7 claves con los valores esperados y, al revertir, el registro vuelve idéntico (`PRIVACY_TWEAK_REVIEW.md`). Para eso se añadió `--tweaks-lab`, que fuera de la VM sale con rc 3. Efecto visible, Windows Home y dominio reales: sin validar. No se añadieron ajustes nuevos (sería una función nueva).
- Juegos: evaluación cerrada en `GAME_PROFILE_RESEARCH.md`. Valorant y Marvel Rivals REJECTED (Marvel lo prohíbe oficialmente); CS2, Rocket League y Apex VERIFIABLE_CANDIDATE con claves corroboradas, **no implementados** en 0.21.0 porque no hay un archivo real del juego en un equipo de prueba para verificar claves y restauración; Siege X RESEARCH_PENDING. Rocket League es el primero para la próxima versión.
- BIOS: solo lectura (`BiosWatch`), sin ninguna API de escritura de firmware. RAM a 5600 MT/s de 6000 nominal: EXPO probablemente apagado. **No se cambió.** Activarlo es decisión tuya en la BIOS, y después conviene una prueba de memoria.

## Clave de firma
`~/.vigia/release-key.pem` (240 B, sin cambios desde el 27-10 23:03 UTC): desde el 30-10 17:35 solo tu cuenta, SYSTEM y Administradores pueden abrirla (`--key-check` rc 0). Hasta entonces `CodexSandboxUsers` tuvo lectura → **POSIBLE EXPOSICIÓN ANTERIOR (sin evidencia de lectura)**. Sin cifrar, en una cuenta administradora con UAC desactivado. No se leyó, copió, movió ni rotó. Firma de producción: solo en un equipo aparte y con tu orden. Detalle: `SECURITY_KEY_REVIEW.md`.

## Otros
- «Fable»: es de JITA (rama `fix/fable-1-v1.5.151` de jita-bot, carpetas `jita-research/fable*`). **No tiene relación con Vigía.**
- `scratch/p5` (fuera del repo): `keyhits.txt` es evidencia del posible incidente de la clave, así que se conserva. El resto son notas de investigación de juegos/privacidad (`*.md`, `*.json`, `msfind.txt`), páginas descargadas (`dl/`, `ms/`, `pages/`) y scripts de inventario de VMs. No se borró nada.
- Selftest antiguo: escribió dos copias en tus datos reales (16:43, «juegos» y «restauración»). Apuntan a un `GameUserSettings.ini` falso de `%TEMP%`, no al de Fortnite. Tus ajustes de prueba (`HKCU\Software\Vigia\SelfTest`) no quedaron. Desde `791fdc9` el selftest se aísla solo.

## Bloqueos (clasificados)
1. **BLOCKED → NEEDS_OWNER_APPROVAL — clave de firma** (`SIGNING_BLOCKED`, P0). Acceso del sandbox a `.vigia` ya retirado (30-10). Queda: posible exposición anterior, clave sin cifrar en una cuenta administradora con UAC desactivado, permisos del sandbox en otras carpetas del perfil. Decidir rotar o mover, firmar fuera de este PC y repetir instalación y migración en VigiaLab con el paquete firmado: `OWNER_DECISIONS_0.21.0.md` §3 y §6.
2. **HARDWARE_VALIDATION_PENDING — temperaturas reales.** La VM no tiene sensores. Procedimiento para tu Ryzen: `SENSOR_RYZEN_VALIDATION.md`. Las notas, el aviso, README y PRODUCT ya no prometen grados con el ayudante abierto (`b7519e6`, `9459165`).
3. **UAC con usuario estándar — VM_TESTED en parte (fila 34); el resto BLOCKED por credenciales.** Comprobado como `estandar` (no administrador): el ayudante lanzado a mano se niega (rc 3), no puede escribir en Program Files, pedir permisos sin escritorio falla limpio («Para esto Vigía necesita permiso…») y no queda ningún proceso. Falta el aviso con contraseña de administrador desde la sesión de `estandar`: exige teclear una contraseña de administrador, y no hay una forma autorizada de hacerlo sin escribirla en comandos o logs. Lo hace el propietario en la VM o en un PC físico.
4. **EXTERNAL_AUDIT_PENDING — servicio SYSTEM.** No va en 0.21.0, así que no bloquea esta versión.
5. **NEEDS_OWNER_APPROVAL:** publicar 0.21.0, merge y push; migración real en tu PC; aprobar los cambios de comportamiento (ayudante a los 10 min, texto de temperatura, servicio fuera del paquete).
6. Hyper-V sigue en `Auto` (autorizado). Para volver: `bcdedit /set {current} hypervisorlaunchtype off` y reiniciar (respaldo en `scratch/p7/bcd-respaldo.bcd`). No lo cambié.
7. ~~Probado solo en simulación~~ → **VM_TESTED (fila 33).** Una tarea de 11 min por el ayudante real, con UAC real, no se cortó: 23 latidos elevados y 132 lecturas automáticas sin error. El ayudante se cerró **600 s** después del fin; ninguna de las 251 lecturas automáticas renovó el plazo. Se volvió a pedir (otro proceso) y con «No» no quedó nada. Límite: `labHold` es una tarea de prueba que no cambia nada; ejercita el mismo control de vida que una operación real, pero no es una operación de producto.

## Versión
**0.21.0** sigue siendo la siguiente versión válida: lo público es 0.20.17 y 0.21.1–0.21.8 (más los paquetes de relevo de la fila 38) solo existieron en VigiaLab con claves de laboratorio. Puente 2.0.1 confía en la misma clave pública y `NextKeys` está vacía: publicarla no corta la actualización ni la recuperación de lo instalado. Candidato, notas, instalación, migración, recuperación y checklist: `RELEASE_CANDIDATE_0.21.0.md`.
