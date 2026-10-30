# Servicio «Sensores de Vigía» — auditoría interna y paquete para revisión externa

Estado: **EXTERNAL_AUDIT_PENDING** · **VM_TESTED** (VigiaLab) · **sin validar en hardware físico** · **no aprobado para instalarse en el PC del propietario**.
Esto es una revisión interna (código leído y pruebas propias, más una pasada de un agente de código en solo lectura). **No es una auditoría externa independiente.**

## 1. Qué es
`VigiaSensores` («Sensores de Vigía»): servicio de Windows que registra Puente durante la instalación. Ejecuta
`"C:\Program Files\Vigia\versiones\<v>\Vigia.exe" --sensor-service` como LocalSystem, lee la temperatura del procesador
con LibreHardwareMonitor + el driver oficial PawnIO y la publica en una memoria compartida de 64 bytes
(`Global\VigiaSensores`). La interfaz de Vigía corre sin administrador y solo **lee** esa memoria.

Código relevante: `app/Vigia/SensorHost.cs` (servicio, publicador y lector), `app/Puente/SensorService.cs` (alta, baja,
parada), `app/Puente/Setup.cs` (`CloseVigia`, `PrepareSensors`, `UninstallFlow`), `app/Vigia/Elevation.cs`
(`InstallTrust`), `app/Vigia/Telemetry.cs` (uso del lector), `tools/sensor-install-contract-test.py`.

## 2. Superficie de ataque
| Canal | ¿Existe? | Detalle |
|---|---|---|
| Tubería con nombre, socket, RPC, COM | **No** | el servicio no escucha nada; no hay forma de mandarle una orden |
| Archivos de entrada / registro | **No** | no lee configuración; no escribe archivos (solo el Visor de eventos, texto propio) |
| Argumentos | Fijos | `--sensor-service`; la ruta la escribe Puente (comillas, solo dentro de `Program Files\Vigia`) |
| Variables de entorno | No influyen | `VIGIA_SENSOR_MAP` solo cambia lo que **lee** la interfaz y solo hacia nombres `Local\VigiaSensoresTest…`; el servicio siempre publica en `Global\VigiaSensores` |
| Memoria compartida | Salida | DACL protegida `O:SY D:P(A;;GA;;;SY)(A;;GR;;;BA)(A;;GR;;;AU)`: escribe solo SYSTEM; Administradores y usuarios autenticados solo leen |
| Control del servicio (SCM) | Por defecto de Windows | usuarios interactivos: consultar; no pueden parar, arrancar ni reconfigurar (probado como `estandar`: acceso denegado) |
| Procesos ajenos | Lectura de ruta | para saber si hay una Vigía abierta mira la ruta del ejecutable de los procesos `Vigia`; solo cuenta si está dentro de la instalación. No abre, inyecta ni escribe en otros procesos |

Como no hay canal de entrada, los puntos «autenticación del cliente», «control de conexiones», «mensajes malformados»,
«datos excesivos» y «cliente de otra cuenta» **no aplican al servicio**. El único dato que cruza la frontera es la memoria
compartida, y quien la valida es el lector (la interfaz sin privilegios), no el servicio.

## 3. Revisión punto por punto
| Punto | Resultado |
|---|---|
| Justificación de LocalSystem | PawnIO solo deja abrir su dispositivo a SYSTEM/Administradores. Sin un proceso con ese nivel, una Vigía sin administrador no ve grados. LocalService/NetworkService no pueden abrir el driver |
| Alternativas de menos privilegio | (a) Ayudante elevado bajo demanda: pediría UAC cada vez que se abre Monitor → descartado por uso. (b) Tarea programada como SYSTEM: mismo privilegio, peor control de ciclo de vida. (c) Cuenta virtual `NT SERVICE\VigiaSensores` con ACL del dispositivo PawnIO a esa SID: exigiría modificar la ACL de un driver de terceros → descartado. (d) **Restringir privilegios del token con `sc privs`**: recomendado (R1), no aplicado porque no se puede comprobar en la VM que LibreHardwareMonitor siga leyendo un Ryzen real con menos privilegios |
| Integridad del ejecutable | El servicio no arranca si su carpeta no está protegida: `InstallTrust.Problem` comprueba que ninguna cuenta que no sea SYSTEM, Administradores o TrustedInstaller pueda escribir (rc 3 y aviso en el Visor de eventos). Puente comprueba SHA-256 de cada archivo contra el manifiesto firmado antes de activar una versión |
| Ubicación y ACL | `C:\Program Files\Vigia\versiones\<v>\`: SYSTEM y Administradores control total; Usuarios y paquetes de apps solo lectura y ejecución; dueño Administradores (probado en VM, #3) |
| Carga de DLL | .NET autocontenido: las DLL salen de la carpeta protegida. El servicio no carga WebView2 |
| Elevación de privilegios | No hay entrada que el servicio procese, así que no hay orden que un usuario pueda colar. No crea procesos (salvo lo interno de .NET), no escribe archivos ni registro, no toca servicios, no instala drivers |
| Suplantación de la memoria | El lector exige dueño SYSTEM o Administradores, cabecera `VGS1` v1, secuencia par estable, hora reciente (≤15 s, no más de 5 s en el futuro) y 1–150 °C; si no, la ignora. El publicador, si encuentra el nombre ya creado por otro dueño, se niega. Probado en VM con `estandar` (#8) |
| Errores | Cada lectura de hardware va en su `try`; un fallo da «sin dato» (NaN), no un número inventado. 30 lecturas vacías → reabre LibreHardwareMonitor (por si PawnIO llegó tarde) |
| Tiempos | Ciclo de 2 s. `sc.exe` desde Puente con límite total de 30 s. Parada: el SCM avisa y el bucle sale en ≤2 s |
| Liberación | `Computer.Close()` al cerrar la última Vigía; vista y handle de la memoria se liberan al salir; objetos `Process` liberados |
| Recuperación | Acciones de fallo: reiniciar a los 60 s (dos veces), contador a cero tras 24 h. Probado matando el proceso (evento 7031, #7) |
| Actualización | Puente para el servicio, comprueba `STOPPED` por número, reapunta y arranca; si no para, no reapunta ni reemplaza archivos en uso (probado #16, #19, #20) |
| Desinstalación | Se para y se borra antes que los archivos (#22) |
| Sin Vigía abierta | No toca el hardware |
| PawnIO | Monitor nunca lo instala; lo instala el instalador con su UAC, solo si falta y sin juego abierto. Al desinstalar solo se da por quitado si su desinstalador oficial terminó bien |

## 4. Pruebas (VigiaLab, Windows 11 Enterprise Evaluation, build 26300, 2026-10-29)
Hechas — ver `VM_VALIDATION_REPORT.md`: instalación (#2, #7), inicio, parada y reinicio (#7), caída y relanzado por el SCM
(#7), permisos del servicio para usuario estándar (#5), suplantación del nombre por otra cuenta (#8), driver ausente
(#9), actualización y vuelta atrás con el servicio (#16, #20), reparación con archivo bloqueado (#19), desinstalación
(#22). Selftest: reutilización del nombre por el mismo dueño, rechazo de otro dueño en publicador y lector.

No aplican (no hay canal de entrada): cliente no autorizado, cliente de otra cuenta, mensajes malformados, datos excesivos, reconexión.

**No probadas:** lectura real de temperatura (la VM no tiene sensores: el servicio publica «sin dato», correcto pero no
demuestra la lectura); driver incompatible; fallo a mitad de una lectura de hardware real; Ryzen físico; Intel físico.

## 5. Hallazgos internos
- Siete fallos de robustez encontrados por una revisión de código en solo lectura (ninguno de escalada); corregidos en `791fdc9` (tabla en `CPU_SENSOR_INTEGRATION_REPORT.md`).
- El servicio se rechazaba a sí mismo como SYSTEM (error 1053) → `1af5284`.
- Reparar con un archivo abierto dejaba el servicio parado → `61a102f`.

## 6. Riesgos pendientes
| # | Riesgo | Gravedad | Estado |
|---|---|---|---|
| R1 | El token del servicio lleva todos los privilegios de SYSTEM; un fallo en LibreHardwareMonitor o PawnIO corre con todo | Media | Recomendado: `sc privs VigiaSensores SeChangeNotifyPrivilege` (+ los que pida el driver) y `sc sidtype VigiaSensores unrestricted`, **tras** probar en un Ryzen físico que sigue leyendo |
| R2 | LibreHardwareMonitor y los módulos de PawnIO son código de terceros dentro del proceso SYSTEM | Media | Fuera del alcance de Vigía; versión fijada en el proyecto |
| R3 | Sin validación en hardware físico | Alta para la función, baja para la seguridad | Requiere autorización para instalar el servicio en un equipo real |
| R4 | Revisión externa independiente no hecha | — | **EXTERNAL_AUDIT_PENDING** |

## 7. Paquete para el revisor externo
Entregar: este documento; `docs/PRIVILEGES.md`; `docs/SECURITY-DESIGN.md`; `VM_VALIDATION_REPORT.md`;
`CPU_SENSOR_INTEGRATION_REPORT.md`; los archivos de código del punto 1; `tools/sensor-install-contract-test.py`.
Preguntas concretas para el revisor: ¿basta la comprobación de dueño + DACL del lector frente a un administrador no
elevado?; ¿qué privilegios mínimos necesita LibreHardwareMonitor con PawnIO (R1)?; ¿la comprobación `InstallTrust` al
arrancar deja una ventana TOCTOU explotable sin ser ya administrador?
