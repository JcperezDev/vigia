# Release readiness — NOT READY
Fecha: 2026-10-29 · rama debloat-v2 · solo commits locales.

## Ejecución verificada
| Prueba | Resultado |
|---|---|
| Build Vigía Release win-x64 | 0 errores, 0 warnings |
| Build Puente Release win-x64 | 0 errores, 0 warnings |
| Publicación self-contained Vigía para laboratorio | rc=0 (anterior a ffe469e; regenerar) |
| Selftest | 329/0 (st70, con servicio de sensores). st71 con BIOS: 332 OK y 7 FAIL de juegos, todos «Cierra Fortnite primero» porque el usuario estaba jugando (guardia correcta); repetir con el juego cerrado |
| UI Apps | 44 OK / 0 fallos, scratch/ui61/ui.txt |
| Puente | 74/0 en tanda actual; no prueba VM |
| release.sh | 31/0 en tanda actual; mock, no publicación |
| Firmas históricas 0.20.17 | ZIP y Puente rc=0 |
| Clave real, solo ACL | rc=3: CodexSandboxUsers tiene RX heredado |

Cambios: fe5f5f2 firma separada; 9f83671 privacidad opcional y traducciones; ffe469e guardia de comandos de laboratorio. No push, merge ni release.

## Checklist obligatorio
- [x] Compilaciones verificadas.
- [x] Selftest, Puente, release mock y UI Apps pasan.
- [ ] Instalación real en VM.
- [ ] Migración desde 0.20.17.
- [ ] UAC y cuenta estándar.
- [ ] ACL y protección real de Program Files.
- [ ] Debloat real y recuperación.
- [ ] Revisión final completa de todos los parámetros privilegiados y staging.
- [ ] Custodia real de firma separada y riesgo histórico resuelto.
- [ ] Rollback real en VM.
- [ ] Revalidación integral de cinco idiomas y diálogo nuevo de confirmación.
- [ ] Auditoría final de regresiones de todas las pantallas y operaciones.
- [x] Limitaciones identificadas sin llamarlas pruebas físicas.

## Riesgos y bloqueos
Hypervisor apagado por BCD del host: no se modifica sin autorización. Clave real accesible por grupo de sandbox: posible exposición, sin evidencia de lectura; no modificada ni rotada. La mitigación de código rechaza firmar con esta ACL, pero NO protege por sí sola el archivo frente a lectores.
La prueba de modelo Hyper-V en LabGuard es una barrera contra errores, no autenticación ni garantía de aislamiento; una VM de producción también podría cumplirla.
Privacidad implementada no equivale a efecto validado en Windows. Revisar además que el mapeo CSP/registro de datos satisfaga el criterio documental antes de promover.
BIOS/UEFI: auditada la build (fuente y DLL compiladas de Vigía y Puente): 0 llamadas a SetFirmwareEnvironmentVariable / NtSetSystemEnvironmentValue, ningún privilegio SeSystemEnvironment, ninguna herramienta ni driver AMI, ningún .sys/.inf/.cat en el repo. Único driver empaquetado: el instalador oficial firmado de PawnIO (sensores). La única acción de firmware es «Reiniciar y entrar a la BIOS» (`shutdown /r /fw`, botón de dos pasos): no escribe nada. Nuevo en solo lectura: `BiosWatch` (comparación con la última lectura; 10 pruebas OK).
Servicio «Sensores de Vigía» (LocalSystem, solo lectura, memoria compartida): `UNIT_TESTED` con publicador de prueba; nunca registrado en un Windows real. Revisión de seguridad independiente en curso; no promover sin VM.

## Qué queda
Informes VM y juegos enumeran pendientes. No se completó validación física, investigación nueva de los seis juegos ni fase BIOS. Auditoría final parcial, no aprobación de seguridad general. No se recomienda READY FOR RELEASE.
