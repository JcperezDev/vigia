# Validación en Windows real (VigiaLab) — PARCIAL
Fecha: 2026-10-29 · rama `debloat-v2` · paquetes de laboratorio construidos con `git archive HEAD` y firmados con una clave **ficticia** (`lab.pem`); el Puente de laboratorio confía solo en esa clave. La clave real no se usó.

Todo lo de abajo se ejecutó de verdad dentro de la VM y se comprobó el estado después. Lo que no está aquí no se probó.

## Laboratorio
| Comprobación | Resultado |
|---|---|
| Hipervisor del host | `hypervisorlaunchtype Auto` (autorizado), `HypervisorPresent=True`, `vmms` y `vmcompute` en marcha |
| VMs | VigiaLab (Running), VigiaIso y VigiaIso2 (Off) |
| Identidad | ID `312758fa-1ded-48de-8da2-1dbb37137bcc`; dentro: `Microsoft Corporation / Virtual Machine`, equipo `VIGIA-LAB` |
| Discos | solo VHDX en `E:\VMs\VigiaLab\`; ningún disco físico en passthrough |
| Red / compartidos | Default Switch (NAT); sin carpetas compartidas; los archivos entran con `Copy-VMFile` (Guest Service Interface) |
| Checkpoints | `limpia` (27-oct), `base-lab`, `v02017-datos`, `mig-0.21.0`, `pre-apps`; se restauraron varias veces sin problema |
| Guardas | todos los scripts comprueban ID y nombre de la VM y la ruta del VHD antes de restaurar o copiar; se pausan si hay un juego abierto en el host |
| LabGuard | `--apps-run` sin `VIGIA_LAB=1` → rc 3 y la app sigue (no se desactivó) |

## Resultados
| # | Prueba | Estado | Evidencia (qué se vio después) |
|---|---|---|---|
| 1 | Instalación de 0.20.17 legítima (base de migración) | VM_VALIDATED | checkpoint `v02017-datos` con datos de prueba y la tarea vieja `\Vigia\Iniciar Vigia` |
| 2 | Migración 0.20.17 → 0.21.0 | VM_VALIDATED (tras 3 arreglos) | setup rc 0 en 17 s; `Programs\Vigia` y `Vigia-Puente` ya no existen; tarea vieja quitada; Run HKCU → `Program Files\Vigia\versiones\0.21.0\Vigia.exe --tray`; accesos de menú y escritorio a Program Files; entrada de desinstalación HKLM; hashes de los datos de usuario iguales antes y después; `estado.json` con `legacy=0.20.17` |
| 3 | ACL de la instalación | VM_VALIDATED | raíz, `Puente.exe` y cada versión: SYSTEM y Administradores control total; Usuarios y paquetes de apps solo lectura y ejecución; dueño Administradores. `ProgramData\Vigia` igual |
| 4 | Inicio sin administrador | PARCIAL | Vigía arranca desde Program Files con la entrada Run (sin tarea con privilegios máximos). No se midió el nivel del token |
| 5 | Usuario estándar (`estandar`, sin admin) | VM_VALIDATED | `--probe` rc 0; puede abrir el mapa de sensores para leer, no para escribir (error 5); `sc stop/start/config VigiaSensores` → acceso denegado |
| 6 | UAC / ayudante con permisos (`--broker`) | BLOCKED | el consentimiento de UAC necesita el escritorio de la VM; PowerShell Direct no tiene escritorio. Necesita que alguien haga clic en la consola de Hyper-V |
| 7 | Servicio «Sensores de Vigía» | VM_VALIDATED (tras arreglo) | LocalSystem, automático, ruta entre comillas a `--sensor-service`; SDDL: usuarios interactivos y de servicio solo consulta. Parar, arrancar y reiniciar: correcto. Proceso matado → el SCM lo relanza (evento 7031, a los 60 s). Lecturas de temperatura: `null` (la VM no tiene sensores físicos; esperado) |
| 8 | Suplantación del mapa compartido | VM_VALIDATED | `estandar` crea `Global\VigiaSensores` con datos falsos (99 °C) con el servicio parado → el servicio no arranca sobre ese mapa y Vigía no muestra el 99; al soltarlo, el servicio vuelve |
| 9 | Servicio sin PawnIO cargado | VM_VALIDATED | driver deshabilitado y VM reiniciada en orden: PawnIO parado, servicio en marcha 70 s después, sin caídas, `--probe` sin lecturas falsas. Driver restaurado a «a petición» |
| 10 | Debloat V2: inventario | VM_VALIDATED | admin: 108 AppX, 4 Win32, 47 aprovisionadas, 8 con dependientes. Estándar: 49 AppX propias, todas `protected/never`; los avisos dicen que todos los usuarios y lo aprovisionado necesitan elevación |
| 11 | Debloat V2: quitar y recuperar (MSN El Tiempo) | VM_VALIDATED | plan → quitar rc 0 (desaparece del usuario, sigue aprovisionada), copia en `copias\…-apps.json`, historial con recuperación A; recuperar rc 0 → vuelve a aparecer |
| 12 | Debloat V2: quitar + aprovisionamiento (MSN Noticias, modo Avanzado) | VM_VALIDATED | desaparece del usuario, de todos y de lo aprovisionado; recuperación B: abre su página en Microsoft Store (se abrió). No se reinstaló desde la Store (necesita cuenta/red de la Store) |
| 13 | Debloat V2: casos negativos | VM_VALIDATED | plan obsoleto → rc 5, nada quitado; app quitada por fuera entre plan y ejecución → rc 5; paquete inexistente → rechazado; App Installer y Microsoft Store → plan sin acción, siguen; aprovisionamiento en modo Seguro → rechazado; framework con dependientes (.NET Native) → rechazado y lista quién depende |
| 14 | Puente: firma mala, firma de otro archivo, paquete incompleto | VM_VALIDATED | rc 1 en los tres; estado, versiones, servicio y Run sin cambios |
| 15 | Actualización cortada (proceso matado) y reparación | VM_VALIDATED | tras el corte sigue en 0.21.0 funcionando; «Reparar» completa a 0.21.1 |
| 16 | Actualizar 0.21.0 → 0.21.1 | VM_VALIDATED | rc 0; servicio y Run apuntan a 0.21.1; ACL igual que la 0.21.0 |
| 17 | Downgrade con manifiesto viejo | VM_VALIDATED | rc 1 «un manifiesto viejo no puede volver atrás»; nada cambia |
| 18 | Archivo dañado + reparar | VM_VALIDATED | se repone; `--install-check` rc 0 |
| 19 | Reparar con un archivo bloqueado | VM_VALIDATED (tras arreglo `61a102f`) | rc 1 «otro programa tiene abierto un archivo; no se cambió nada»; versión y servicio intactos |
| 20 | Volver a la anterior (rollback) | VM_VALIDATED | 0.21.1 → 0.21.0; servicio y Run a 0.21.0; `--install-check` rc 0 |
| 21 | Dos Puentes a la vez | VM_VALIDATED | el primero instala (rc 0), el segundo se niega (rc 1); sin restos de `preparando`/`apartada` |
| 22 | Desinstalar (Puente instalado y `Vigia-Setup.exe`) | VM_VALIDATED (tras 2 arreglos) | Program Files, servicio, Run, HKLM, accesos y tareas: fuera. Datos de usuario: 148 → 147 archivos (solo se va la marca `pawnio.json`). PawnIO: su desinstalador oficial deja el driver registrado y parado; Vigía ya no dice «quitado» si su desinstalador falla |

## Fallos encontrados en la VM y arreglados
`4dbe8c8` install-check pedía `es.json` · `1894d15` la tarea vieja con privilegios máximos no se reconocía (XML leído con la página de códigos equivocada) y la sesión 0 se tomaba por «juego abierto» · `1af5284` el servicio se rechazaba a sí mismo como SYSTEM (error 1053) · `61a102f` reparar con un archivo abierto dejaba el servicio parado · `03ad4c4` PawnIO se daba por quitado sin mirar el resultado · `048de17` desinstalar desde el instalador dejaba la carpeta vacía.

## Sin probar en VM
UAC/ayudante con consentimiento (#6), red cortada a mitad de descarga real (solo el corte del proceso), disco lleno, reinstalación desde Microsoft Store, Win32 con su desinstalador (no se ejecuta solo, por diseño), lectura de temperaturas reales (la VM no tiene sensores). Esto último queda para hardware físico autorizado.
