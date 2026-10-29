// Revisión del catálogo (auditoría 2026-10): añade a cada ajuste su estado y la fuente oficial que lo respalda,
// corrige explicaciones que prometían más de lo que hace Windows y retira lo que no tiene beneficio demostrable.
// Nunca cambia el orden ni el contenido de los «ops»: «Revertir» guarda el valor original por posición.
// Uso: node tools/tweaks-review.js   (idempotente: se puede volver a correr)
'use strict';
const fs = require('fs');
const path = require('path');
const file = path.join(__dirname, '..', 'app', 'Vigia', 'tweaks.json');
const list = JSON.parse(fs.readFileSync(file, 'utf8'));

const MS = 'https://learn.microsoft.com/en-us/';
const MMCSS = MS + 'windows/win32/procthread/multimedia-class-scheduler-service';
// status: recommended | optional | experimental | deprecated | unsupported
const R = {
  'power-ultimate': { status: 'experimental',
    detail: 'Crea el plan «Vigía»: enchufado, el procesador no baja del 100 % aunque no haga nada. Más consumo y temperatura en reposo; el plan Equilibrado ya sube de velocidad al instante en la mayoría de PCs. Pruébalo midiendo antes y después. Tu plan de antes vuelve con «Deshacer».' },
  'mmcss-games': { status: 'experimental', source: MMCSS,
    detail: 'Sube la prioridad de la tarea «Games» del programador multimedia de Windows. Solo afecta a los juegos que se registran en ese servicio (pocos lo hacen); en los demás no cambia nada. Microsoft indica que «GPU Priority» aún no se usa.' },
  'responsiveness': { status: 'deprecated', retired: true, source: MMCSS,
    detail: 'Retirado: según Microsoft, SystemResponsiveness por debajo de 10 se trata como 20 (el valor de siempre), así que ponerlo en 0 no cambia nada. Si Vigía lo aplicó antes, se deshace solo.' },
  'priority-separation': { status: 'experimental', source: MS + 'windows/win32/procthread/scheduling-priorities',
    detail: 'Cambia cómo reparte Windows el tiempo de procesador entre la ventana activa y el resto. No hay mediciones públicas que muestren mejora en juegos; compáralo midiendo FPS antes y después.' },
  'svchost-split': { status: 'deprecated', retired: true, source: MS + 'windows/application-management/svchost-service-refactoring',
    detail: 'Retirado: agrupa servicios en menos procesos para que la RAM parezca más baja, pero Microsoft los separa a propósito para que un fallo no tumbe a los demás. Si Vigía lo aplicó antes, se deshace solo.' },
  'hibernate-off': { status: 'optional', group: 'Almacenamiento', source: MS + 'troubleshoot/windows-client/setup-upgrade-and-drivers/disable-and-re-enable-hibernation',
    detail: 'Borra el archivo de hibernación (hiberfil.sys) y libera ese espacio en C:. A cambio no puedes hibernar y se apaga el inicio rápido de Windows, así que el arranque en frío puede tardar un poco más.' },
  'game-dvr-off': { status: 'optional' },
  'background-apps': { status: 'optional',
    detail: null },
  'transparency-off': { status: 'optional' },
  'mouse-raw': { status: 'optional', source: MS + 'windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement',
    detail: 'Quita «Mejorar la precisión del puntero» (la aceleración del ratón de Windows): la misma distancia de mano mueve siempre lo mismo. Los juegos que leen el ratón con Raw Input ya la ignoran; esto cambia el escritorio y los juegos que no lo usan.' },
  'accessibility-keys': { status: 'optional' },
  'input-queue': { status: 'deprecated' },
  'shutdown-fast': { status: 'deprecated' },
  'end-task': { status: 'optional' },
  'suggestions-off': { status: 'optional' },
  'telemetry-off': { status: 'optional', source: MS + 'windows/privacy/configure-windows-diagnostic-data-in-your-organization',
    detail: 'Privacidad, no rendimiento: pide a Windows enviar el mínimo de datos de diagnóstico y apaga el servicio que los envía. En Home y Pro Windows sigue enviando los datos obligatorios; solo Enterprise y Education llegan a cero.' },
  'telemetry-tasks': { status: 'optional',
    detail: null },
  'activity-off': { status: 'optional', source: MS + 'windows/client-management/mdm/policy-csp-privacy' },
  'services-unused': { status: 'deprecated', retired: true,
    detail: 'Retirado: apagaba diez servicios de una vez, entre ellos los de informes de errores y compatibilidad que sirven para diagnosticar. Los servicios en «Manual» no gastan nada mientras no se usan. Si Vigía lo aplicó antes, se deshace solo.' },
  'print-off': { status: 'optional', source: 'https://msrc.microsoft.com/update-guide/vulnerability/CVE-2021-34527' },
  'services-xbox': { status: 'deprecated' },
  'search-off': { status: 'deprecated' },
  'update-control': { status: 'optional', source: MS + 'windows/deployment/update/waas-wu-settings' },
  'gpu-high': { status: 'experimental',
    detail: 'Pide a Windows la GPU de alto rendimiento para todos los programas. Solo cambia algo en equipos con dos GPU (portátiles con gráfica integrada y dedicada); en un PC con una sola GPU no hace nada. Mejor elegir la GPU por juego en Configuración de Windows > Gráficos.' },
  'windowed-opt': { status: 'optional' },
  'hags': { status: 'optional', source: 'https://devblogs.microsoft.com/directx/hardware-accelerated-gpu-scheduling/' },
  'game-mode': { status: 'recommended' },
  'animations-off': { status: 'optional' },
  'startup-delay': { status: 'experimental',
    detail: 'Quita la espera que hace Windows antes de abrir los programas de inicio. Son valores no documentados por Microsoft: el escritorio puede tardar más en responder si muchos programas arrancan a la vez.' },
  'boot-info': { status: 'optional', source: MS + 'troubleshoot/windows-server/performance/enable-verbose-startup-shutdown-logon-logoff-status-messages' },
  'smb1-off': { status: 'recommended', source: MS + 'windows-server/storage/file-server/troubleshoot/detect-enable-and-disable-smbv1-v2-v3' },
  'browsers-bg': { status: 'optional', source: MS + 'deployedge/microsoft-edge-policies' },
  'ad-id-off': { status: 'optional', source: MS + 'windows/client-management/mdm/policy-csp-privacy' },
  'firefox-telemetry': { status: 'optional', source: 'https://mozilla.github.io/policy-templates/' },
  'wer-off': { status: 'optional', source: MS + 'windows/win32/wer/wer-settings',
    detail: null },
  'p2p-updates-off': { status: 'optional', source: MS + 'windows/deployment/do/waas-delivery-optimization-reference' },
  'usb-suspend-off': { status: 'experimental', source: MS + 'windows-hardware/drivers/usbcon/usb-selective-suspend',
    detail: 'Windows deja de dormir los puertos USB que no se usan. Solo sirve si el ratón, el teclado o los cascos se desconectan un segundo o tardan en responder; si no te pasa, no cambia nada y gasta un poco más. Solo enchufado.' },
  'pcie-aspm-off': { status: 'experimental', source: MS + 'windows-hardware/customize/power-settings/pci-express-settings-link-state-power-management',
    detail: 'Windows deja de poner en ahorro la conexión PCI Express (gráfica, NVMe) entre usos. Solo sirve si un dispositivo PCIe falla o da tirones al despertar; si no, gasta más energía en reposo sin ganar nada. Solo enchufado.' },
  'vrr-windowed': { status: 'optional', source: 'https://devblogs.microsoft.com/directx/os-variable-refresh-rate/' },
  'widgets-off': { status: 'optional', source: MS + 'windows/client-management/mdm/policy-csp-newsandinterests' },
  'start-web-off': { status: 'optional', source: MS + 'windows/client-management/mdm/policy-csp-search' },
  'recall-off': { status: 'optional', source: MS + 'windows/client-management/manage-recall' },
};
// textos que solo reciben una advertencia al final (sin reescribir lo que ya estaba bien)
const SUFFIX = {
  'wer-off': ' Ojo: sin informes, Vigía y Windows tienen menos datos para explicar por qué se cerró un programa.',
};

const seen = new Set();
for (const t of list) {
  const r = R[t.id];
  if (!r) throw new Error(`sin revisión: ${t.id}`);
  seen.add(t.id);
  t.status = r.status;
  if (r.source) t.source = r.source;
  if (r.group) t.group = r.group;
  if (r.detail) t.detail = r.detail;
  if (SUFFIX[t.id] && !t.detail.endsWith(SUFFIX[t.id])) t.detail += SUFFIX[t.id];
  if (r.retired) t.retired = true;
  // los retirados nunca vuelven a aplicarse: el estado debe decirlo
  if (t.retired && t.status !== 'deprecated') throw new Error(`${t.id}: retirado pero no deprecated`);
}
for (const id of Object.keys(R)) if (!seen.has(id)) throw new Error(`revisión para un id que no existe: ${id}`);

// mismo formato que antes: 2 espacios, CRLF y salto final
fs.writeFileSync(file, JSON.stringify(list, null, 2).replace(/\n/g, '\r\n') + '\r\n');
const by = {};
for (const t of list) by[t.status] = (by[t.status] || 0) + 1;
console.log(`${list.length} ajustes; activos ${list.filter((t) => !t.retired).length}; retirados ${list.filter((t) => t.retired).length};`,
  `con fuente ${list.filter((t) => t.source).length};`, JSON.stringify(by));
