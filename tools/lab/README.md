# Laboratorio (VM VigiaLab)

Pruebas de lo arriesgado en una VM de Hyper-V, nunca en el PC real. La contraseña de la VM vive en `%USERPROFILE%\.vigia\vm-cred.txt` (fuera del repo).

1. `make-unattend.ps1` y `make-vm.ps1`: crean la VM con Windows 11 desatendido.
2. `vmboot.ps1`: la arranca (pulsa una tecla para arrancar del DVD). `vmwait.ps1`: espera a que Windows esté listo.
3. `labtest.ps1` (con `VIGIA_LAB_DIR` apuntando a la carpeta con `vigia-lab\` publicado): checkpoint «limpia», copia original, aplica los 35 ajustes, quita las apps, reinicia, revierte y restaura la copia original. Resultados en `lab\`.
4. `vmshot.ps1`: captura de la pantalla de la VM sin abrir ventanas.

Requisitos en el host: NX y SVM activados en la BIOS, `hypervisorlaunchtype Auto` y el driver `Vid` con inicio `system`.
