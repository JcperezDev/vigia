// Uso: node tools/labguard-host-test.js <Vigia.exe> <carpeta de salida vacía>
// Se corre en un PC que NO es la VM de pruebas. Con VIGIA_LAB=1 y LAB_ALLOW_GAME=1 (lo que se pone en el laboratorio),
// cada orden de laboratorio tiene que negarse sin cambiar nada y sin abrir la interfaz. Los ids no existen y los datos de
// Vigía van a una carpeta aislada: aunque la guarda fallara, no hay nada real que tocar.
// También comprueba que una orden incompleta sale con error en vez de arrancar Vigía.
'use strict';
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const [, , exe, outDir] = process.argv;
if (!exe || !outDir) { console.error('uso: labguard-host-test.js <Vigia.exe> <salida>'); process.exit(2); }
fs.mkdirSync(path.join(outDir, 'data'), { recursive: true });
const o = (n) => path.join(path.resolve(outDir), n);
const env = { ...process.env, VIGIA_DATA: o('data'), VIGIA_LAB: '1', LAB_ALLOW_GAME: '1' };
let fails = 0; const out = [];
const runKey = () => spawnSync('reg', ['query', 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'], { encoding: 'utf8', windowsHide: true }).stdout || '';
const runBefore = runKey();
const check = (ok, what, extra = '') => { out.push(`${ok ? 'OK  ' : 'FAIL'} ${what}${extra ? ` (${extra})` : ''}`); if (!ok) fails++; };

// rc esperado: 3 = la guarda lo negó; 2 = incompleta (o solo de laboratorio) y no se hizo nada.
const cases = [
  [['--lab', 'apply', 'no-existe', o('lab.json')], [3]],
  [['--lab', 'debloat', 'no-existe', o('lab2.json')], [3]],
  [['--tweaks-lab', 'apply', 'no-existe', o('tw.json')], [3]],
  [['--tweaks-lab', 'revert', 'no-existe', o('tw2.json')], [3]],
  [['--apps-run', 'no-existe', 'huella', o('run.json')], [3]],
  [['--apps-restore', 'no-existe', '-', o('rest.json')], [3]],
  // --autostart review solo lee; --startup-offall y --simple-run no se lanzan en el host (si la guarda fallara cambiarían
  // cosas reales): usan la misma LabGuard.Allowed, comprobada aquí con las demás.
  [['--autostart', 'review', o('auto.json')], [2]],
  // incompletas: antes arrancaban la interfaz
  [['--apps-run'], [2]], [['--apps-run', 'x'], [2]], [['--apps-restore', 'x'], [2]],
  [['--tweaks-lab', 'apply'], [2]], [['--lab', 'apply'], [2]], [['--usb'], [2]], [['--iso', 'x'], [2]],
];
for (const [args, want] of cases) {
  const t0 = Date.now();
  const r = spawnSync(exe, args, { env, timeout: 60000, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], encoding: 'utf8' });
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  const timedOut = r.error && r.error.code === 'ETIMEDOUT';
  check(!timedOut && want.includes(r.status), `${args.slice(0, 2).join(' ')} -> rc ${want.join('/')}`, `rc=${r.status} ${secs}s${timedOut ? ' (se quedó abierta: arrancó la interfaz)' : ''}`);
  const file = args.find((a) => a.startsWith(path.resolve(outDir)) && a.endsWith('.json'));
  if (file) check(!fs.existsSync(file), `${args[0]}: no escribió resultado (no hizo nada)`);
}
check(runKey() === runBefore, 'el inicio con Windows del usuario (Run) quedó igual');
console.log(out.join('\n'));
console.log(`labguard host OK=${out.length - fails} FAIL=${fails}`);
process.exit(fails ? 1 : 0);
