// Genera docs/TWEAKS.md desde app/Vigia/tweaks.json: qué cambia cada ajuste, estado, fuente y cómo se deshace.
// Los textos salen en inglés con wwwroot/i18n/en.json. Uso: node tools/tweaks-doc.js
'use strict';
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..');
const read = (p) => JSON.parse(fs.readFileSync(path.join(root, p), 'utf8').replace(/^\uFEFF/, ''));
const tw = read('app/Vigia/tweaks.json');
const en = read('app/Vigia/wwwroot/i18n/en.json');
const T = (s) => (s && en[s]) || s || '';
const cell = (s) => String(s).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');

function op(o) {
  if (o.reg && o.kv) return `\`${o.reg}\` → \`${o.name}\`: key \`${o.kv}=${o.value}\` inside the string`;
  if (o.reg && o.delete) return `delete \`${o.reg}\\${o.name}\``;
  if (o.reg) return `\`${o.reg}\\${o.name}\` = \`${o.value}\` (${o.type || 'dword'})`;
  if (o.svc) return `service \`${o.svc}\` start = ${o.start}`;
  if (o.task) return `scheduled task \`${o.task}\` enabled = ${o.enabled}`;
  if (o.power) return `power setting \`${o.power}\` (AC) = ${o.ac}`;
  if (o.special) return `special: \`${o.special}\``;
  return '`' + JSON.stringify(o) + '`';
}
const undo = {
  reg: 'the exact previous value (or its absence) is restored',
  svc: 'the previous start type is restored',
  task: 'the task goes back to its previous enabled state',
  power: 'the previous value is set again on the plan that was active',
};
function undoOf(t) {
  const kinds = new Set(t.ops.map((o) => (o.reg ? 'reg' : o.svc ? 'svc' : o.task ? 'task' : o.power ? 'power' : 'special:' + o.special)));
  return [...kinds].map((k) => undo[k] || ({
    'special:power-ultimate': 'switches back to the previous plan and deletes the Vigía plan if Vigía created it',
    'special:hibernate-off': 'turns hibernation back on if it was on',
    'special:dynamictick': 'removes the boot setting if Vigía added it',
    'special:mouse-refresh': 'nothing to undo (only tells Windows to reload mouse settings)',
  })[k] || k).join('; ');
}

const counts = {};
tw.forEach((t) => (counts[t.status] = (counts[t.status] || 0) + 1));
let md = `# Vigía tweak catalog\n\n`;
md += `Generated from \`app/Vigia/tweaks.json\` by \`node tools/tweaks-doc.js\`. Do not edit by hand.\n\n`;
md += `${tw.length} entries: ` + Object.entries(counts).map(([k, v]) => `${v} ${k}`).join(', ') + `; ${tw.filter((t) => t.source).length} with an official source.\n\n`;
md += `**Statuses.** \`recommended\`: documented and safe for most PCs. \`optional\`: documented, depends on the user. \`experimental\`: effect depends on hardware or game; measure before and after; never in Simple mode or "Apply all". \`deprecated\`: retired; only its undo remains, so earlier users can get their original values back.\n\n`;
md += `**Validation.** "Tested" means the apply/undo cycle was run on a real PC by the maintainer. The engine itself (backup, apply, read back, undo, rollback of a half-applied tweak) is covered by \`Vigia.exe --selftest\` on a test registry key. No entry claims an FPS or latency gain.\n\n`;
md += `**How undo works.** Before the first apply, Vigía saves the exact previous value of every operation and makes a restore point. "Revert" restores those values. If an operation fails halfway, the operations already done in that attempt are undone right away.\n\n`;
md += `| Id | Tweak | Status | Reboot | Tested | Source |\n|---|---|---|---|---|---|\n`;
for (const t of tw) md += `| \`${t.id}\` | ${cell(T(t.title))} | ${t.status} | ${t.reboot ? 'yes' : 'no'} | ${t.tested ? 'yes' : 'no'} | ${t.source ? `[link](${t.source})` : '—'} |\n`;
md += `\n## Details\n`;
for (const t of tw) {
  md += `\n### \`${t.id}\` — ${T(t.title)}\n\n`;
  md += `- **Status:** ${t.status}${t.retired ? ' (retired: not offered; undo only)' : ''}\n`;
  md += `- **Group:** ${T(t.group)}\n`;
  md += `- **What it does:** ${T(t.detail)}\n`;
  md += `- **Changes:**\n` + t.ops.map((o) => `  - ${op(o)}`).join('\n') + '\n';
  md += `- **Undo:** ${undoOf(t)}\n`;
  md += `- **Reboot needed:** ${t.reboot ? 'yes' : 'no'}\n`;
  md += `- **Evidence:** ${t.source ? `<${t.source}>` : 'no official source yet; treat as unverified'}\n`;
}
fs.mkdirSync(path.join(root, 'docs'), { recursive: true });
fs.writeFileSync(path.join(root, 'docs', 'TWEAKS.md'), md);
console.log(`docs/TWEAKS.md: ${tw.length} ajustes`);
