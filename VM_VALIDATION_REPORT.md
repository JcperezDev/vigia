# Validación Windows real — BLOCKED
Fecha: 2026-10-29. Rama debloat-v2. No hay resultado VM_TESTED.

## Evidencia ejecutada
- Hyper-V inventariado: VigiaLab (4 CPU, 8 GB, disco virtual 100 GB en E:), VigiaIso y VigiaIso2. Solo VigiaLab tenía checkpoint `limpia` (27-oct).
- VigiaLab ID: 312758fa-1ded-48de-8da2-1dbb37137bcc. Sin disco físico conectado según Get-VMHardDiskDrive.
- Se restauró `limpia` y se desconectaron sus dos DVD de instalación. El intento Start-VM falló: «hypervisor is not running». Estado posterior Saved; checkpoint sigue existiendo.
- bcdedit /enum {current}: hypervisorlaunchtype Off. Win32_ComputerSystem.HypervisorPresent=False. No se cambió BCD, BIOS ni seguridad del host.
- No se logró entrar al invitado. Identidad interna, datos exclusivamente de prueba, aislamiento de red y ausencia de rutas compartidas NO confirmados. Default Switch no demuestra aislamiento de producción.

## Artefactos preparados, no ejecutados en la VM
`scratch/lab`: publicación self-contained de Vigía 0.21.0, Puente-test y paquete firmado con clave ficticia; no usa la privada real. El paquete es anterior al commit ffe469e: regenerarlo antes de probar.
0.20.17 descargado del release público legítimo: ZIP y Puente verificaron firma con el Puente de producción recompilado (ambos rc=0). Esto no es una instalación probada.
Scripts scratch/lab/ps son preparación local, no un harness de aceptación final. No reutilizar start.ps1 sin añadir aislamiento y preservación del estado: restaura un checkpoint.

## Pendiente obligatorio
1. Autorización para resolver el arranque del hipervisor del host, o proporcionar otro laboratorio operativo. No hacerlo automáticamente.
2. Dentro del invitado: confirmar nombre VIGIA-LAB, datos de prueba, discos virtuales y ausencia de comparticiones; aislar red antes de mutaciones.
3. Comprobar restauración y registrar baseline del invitado.
4. Instalación limpia, migración histórica, administrador/estándar, UAC activo, ACL Program Files, IPC y datos preservados.
5. Fallos de instalación/actualización/rollback, espacio, firmas, manifiestos, concurrencia y permisos.
6. Debloat real: plan explícito, app de prueba recuperable, historial y recuperación comprobada; rechazos de Store, frameworks, App Installer y dependencias; otros usuarios y aprovisionamiento separados.

Próximo paso: laboratorio operativo. No llamar VM_TESTED a los 74 tests de Puente ni a mocks.
