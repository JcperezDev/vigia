# Vigía — informe final de validación (rama `debloat-v2`)
Fecha: 2026-10-30 (actualizado 00:45) · **Estado: NOT READY · `SIGNING_BLOCKED`** (motivos en «Bloqueos»). Candidato local sin firmar: `RELEASE_CANDIDATE_0.21.0.md`.
Solo commits locales: sin push, merge ni release. La instalación principal del PC no se tocó.

## Commits
- Commit inicial de esta tanda: `ed66d5c` (BIOS solo lectura).
- Creados: `791fdc9` sensores más robustos (los siete arreglos de la revisión) · `4dbe8c8` install-check sin `es.json` · `1894d15` migración de la tarea vieja y sesión 0 · `1af5284` servicio como SYSTEM · `61a102f` reparación con archivo abierto · `bf0a2f8` idiomas (85 mensajes) · `6014411` Apps por consola solo en VM · `03ad4c4` PawnIO al desinstalar · `048de17` carpeta vacía al desinstalar · `e0cde22` este informe · `17ad7e8` ayudante probado en VM y juegos evaluados · `81ac14c` privacidad aplicada y revertida en VM (`--tweaks-lab`) · `7ece97d` informe · `c160702` relevo de claves · `881d769` reauditoría de la clave (`SIGNING_BLOCKED`) · `cf20c0f` ayudante con cierre por inactividad y motivo visible al cancelar UAC · `04f5ffb` las lecturas de sensores ya no mantienen vivo el ayudante; `SENSOR_SERVICE_AUDIT.md` · `97b9c29` tampoco la memoria automática; aviso de temperatura; compatibilidad y anti-rollback de la firma · `14b506d` ventana en negro al abrir tras arrancar con Windows · `34d03df` UAC real en VM · `bfa46c2` plazo del ayudante desde el último pedido, servicio fuera de la versión pública, temperatura no disponible · `dd36490` Puente 2.0.1 · `3aec484` Puente quita el servicio antes de PawnIO; Monitor descarta 0 °C y fuera de rango; prueba del Monitor · `3629a2c` contrato · `9db877f` LabGuard en Apps/`--autostart`/`--msdl-probe` y órdenes incompletas sin abrir la app · `c260497` contrato de `LAB_ALLOW_GAME` · `c82336b` informes de clave y revisión externa.
- Archivos de código tocados: `app/Puente/Setup.cs`, `app/Puente/SensorService.cs`, `app/Puente/Program.cs`, `app/Vigia/SensorHost.cs`, `Elevation.cs`, `AutoStart.cs`, `Telemetry.cs`, `Updates.cs`, `Program.cs`, `wwwroot/i18n/*`, `tools/i18n-extract.js`, `tools/i18n-check.js`, `tools/i18n-add-mensajes.js`, `tools/sensor-install-contract-test.py`, `AdminOps.cs`, `Tray.cs`, `wwwroot/app.js`, `tools/puente-test.js`.

## Pruebas automáticas (código actual, compilado de cero)
Última tanda: suite `s25` sobre `c260497` (2026-10-30 00:27–00:28). Las cifras anteriores son históricas.

| Comando | Resultado |
|---|---|
| `dotnet build` Vigía / Puente / Puente-test (Release win-x64) | rc 0, 0 warnings, 0 errores |
| `Vigia.exe --selftest` con `VIGIA_DATA` aislado | **358 OK / 0 FAIL** (las suites anteriores decían 359: contaban una línea informativa) |
| `node tools/puente-test.js` | 76 / 0 (incluye relevo de claves) |
| `node tools/release-test.js` (GitHub simulado) | 31 / 0 |
| `python -I tools/sensor-install-contract-test.py` | OK |
| `node tools/i18n-check.js` | pt/fr/de: 2435 claves, 0 errores, 0 distintas |
| `node tools/ui-apps-test.js` (Chrome sin ventana) | 44 / 0 |
| `node tools/ui-monitor-test.js` (nueva) | 71 / 0 · cinco idiomas · con el `app.js` anterior: 45 FAIL |
| `node tools/labguard-host-test.js` en este PC (nueva) | 22 / 0 con `VIGIA_LAB=1` y `LAB_ALLOW_GAME=1` |
| `scratch/lab/test/guard-test.ps1` (guarda de los scripts de VM) | 10 / 0 · con la versión anterior: 10 FAIL |
| `Vigia.exe --sensor-service` en la compilación pública | rc 2 (no existe) |
| Logs de la suite con material de clave privada | 0 |
| Catálogos JSON | todos parsean |

## Pruebas en VM (reales, no simuladas)
Detalle en `VM_VALIDATION_REPORT.md`. Las 28 de baseline se conservan; se añadieron 29–32 solo para lo que cambió (servicio fuera, Puente 2.0.1, Monitor, cierre del ayudante con Monitor abierto, nueva solicitud). 32 pruebas, todas VM_VALIDATED (varias tras arreglo). UAC real en la consola de la VM: abrir Vigía no pide UAC; una operación administrativa saca el aviso en el escritorio seguro; «No» deja la interfaz sin elevar y la página explica por qué; «Sí» levanta el ayudante y la interfaz sigue sin elevar; el ayudante se cierra solo a los 10 min sin pedidos aunque Monitor siga leyendo (antes no); tras reiniciar, Vigía abre sin pantalla negra (antes sí). También: privacidad (aplicar y revertir), ayudante `--broker` (8 rechazos e IPC), migración 0.20.17 → 0.21.0, ACL, usuario estándar, servicio, suplantación del mapa, servicio sin driver, Debloat (quitar, recuperar, aprovisionamiento, casos negativos), firmas malas, paquete incompleto, actualización cortada, downgrade, reparación, archivo bloqueado, rollback, dos Puentes, desinstalación. **La VM no tiene sensores: ninguna temperatura física se validó en la VM.**

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
`~/.vigia/release-key.pem`: el grupo `CodexSandboxUsers` tiene ReadAndExecute **explícito** sobre `~/.vigia` (revisión de metadatos del 30-10; el informe anterior decía «heredada») → **POSIBLE EXPOSICIÓN (sin evidencia de lectura)**. No se leyó, copió, rotó ni borró. Firmar con esa ACL está bloqueado por código (`--key-check` rc 3). El procedimiento de rotación está en `SECURITY_KEY_REVIEW.md` y necesita tu intervención.

## Otros
- «Fable»: es de JITA (rama `fix/fable-1-v1.5.151` de jita-bot, carpetas `jita-research/fable*`). **No tiene relación con Vigía.**
- `scratch/p5` (fuera del repo): `keyhits.txt` es evidencia del posible incidente de la clave, así que se conserva. El resto son notas de investigación de juegos/privacidad (`*.md`, `*.json`, `msfind.txt`), páginas descargadas (`dl/`, `ms/`, `pages/`) y scripts de inventario de VMs. No se borró nada.
- Selftest antiguo: escribió dos copias en tus datos reales (16:43, «juegos» y «restauración»). Apuntan a un `GameUserSettings.ini` falso de `%TEMP%`, no al de Fortnite. Tus ajustes de prueba (`HKCU\Software\Vigia\SelfTest`) no quedaron. Desde `791fdc9` el selftest se aísla solo.

## Bloqueos (clasificados)
1. **BLOCKED → NEEDS_OWNER_APPROVAL — clave de firma** (`SIGNING_BLOCKED`, P0). `CodexSandboxUsers` tiene ReadAndExecute explícito sobre `~/.vigia` y la clave está sin cifrar; no hay prueba de lectura, pero no se puede descartar. Retirar el acceso, custodiarla en `vigia-firma` y decidir la rotación: procedimiento exacto en `SECURITY_KEY_REVIEW.md` (A/B/C), sin ejecutar. Cambiar permisos o la clave necesita tu autorización.
2. **HARDWARE_VALIDATION_PENDING — temperaturas reales.** La VM no tiene sensores. Afecta además a la frase «los grados se ven mientras el ayudante está abierto» de las notas.
3. **HARDWARE_VALIDATION_PENDING / NEEDS_OWNER_APPROVAL — UAC con usuario estándar en un PC físico** (pide la contraseña de un administrador; no se tecleó ninguna). Con administrador está probado en VM.
4. **EXTERNAL_AUDIT_PENDING — servicio SYSTEM.** No va en 0.21.0, así que no bloquea esta versión.
5. **NEEDS_OWNER_APPROVAL:** publicar 0.21.0, merge y push; migración real en tu PC; aprobar los cambios de comportamiento (ayudante a los 10 min, texto de temperatura, servicio fuera del paquete).
6. Hyper-V sigue en `Auto` (autorizado). Para volver: `bcdedit /set {current} hypervisorlaunchtype off` y reiniciar (respaldo en `scratch/p7/bcd-respaldo.bcd`). No lo cambié.
7. Probado solo en simulación: que el plazo de 10 min no corte una tarea administrativa real de más de 10 minutos (selftest con plazo de 400 ms). En VM se comprobó el cierre y la nueva solicitud, no una tarea tan larga.

## Versión
**0.21.0** sigue siendo la siguiente versión válida: lo público es 0.20.17 y 0.21.1–0.21.8 solo existieron en VigiaLab con clave de laboratorio. Puente 2.0.1 confía en la misma clave pública y `NextKeys` está vacía: publicarla no corta la actualización ni la recuperación de lo instalado. Candidato, notas, instalación, migración, recuperación y checklist: `RELEASE_CANDIDATE_0.21.0.md`.
