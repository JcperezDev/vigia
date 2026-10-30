#!/usr/bin/env node
// Comprueba un candidato sin firmar (out/<versión>, de VIGIA_RELEASE_SIGN=fuera tools/release.sh) antes de llevarlo a firmar:
//   * salió de este código (candidato.txt: commit, y app/ no cambió desde él) y sus huellas son las que se probaron;
//   * el manifiesto cuadra con el zip y con Puente.exe (tamaño, SHA-256, versión), y Vigia-Setup.exe es el mismo Puente;
//   * la versión de Puente es la de Puente.csproj y es mayor que la de la última versión publicada (si no, no se instala);
//   * es la compilación pública (sin comandos de prueba ni servicio de sensores) y lleva la clave pública de Vigía;
//   * Vigía dentro del zip: versión, --install-check, arreglo de ubicación (LocationGuard, sin listas de redes ni BSSID).
// No usa ni abre ninguna clave privada. Uso: node tools/candidate-check.js [out/0.21.0]
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..');
const V = /<Version>([^<]+)<\/Version>/.exec(fs.readFileSync(path.join(ROOT, 'app/Vigia/Vigia.csproj'), 'utf8'))[1];
const OUT = path.resolve(process.argv[2] || path.join(ROOT, 'out', V));
let ok = 0, fail = 0;
const check = (c, name, extra) => { if (c) { ok++; console.log('OK   ' + name); } else { fail++; console.log('FAIL ' + name + (extra ? '\n     ' + String(extra).slice(-600).replace(/\n/g, '\n     ') : '')); } };
const sh = (exe, args, o = {}) => { const r = spawnSync(exe, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true, timeout: 120000, ...o }); return { code: r.status, out: (r.stdout || '') + (r.stderr || '') }; };
const git = (...a) => sh('git', ['-C', ROOT, ...a]);
const sha = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const fileVersion = f => sh('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(Get-Item -LiteralPath '${f.replace(/'/g, "''")}').VersionInfo.FileVersion`]).out.trim();
const csprojVersion = txt => (/<Version>([^<]+)<\/Version>/.exec(txt) || [])[1];
const vcmp = (a, b) => { const x = a.split('.').map(Number), y = b.split('.').map(Number); for (let i = 0; i < 4; i++) { const d = (x[i] || 0) - (y[i] || 0); if (d) return d; } return 0; };

const ZIP = `Vigia-${V}-win-x64.zip`;
const f = n => path.join(OUT, n);
for (const n of [ZIP, 'Puente.exe', 'Vigia-Setup.exe', 'vigia-manifest.json']) if (!fs.existsSync(f(n))) { console.error('Falta ' + f(n)); process.exit(2); }

console.log(`--- candidato ${V} en ${OUT}`);
// 1. de qué código salió
const cand = fs.existsSync(f('candidato.txt')) ? fs.readFileSync(f('candidato.txt'), 'utf8') : '';
const commit = (/^commit ([0-9a-f]{40})$/m.exec(cand) || [])[1];
check(!!commit && git('cat-file', '-e', commit + '^{commit}').code === 0, 'candidato.txt dice de qué commit salió' + (commit ? ` (${commit.slice(0, 7)})` : ''), cand || 'sin candidato.txt');
if (commit) {
  const d = git('diff', '--stat', commit, 'HEAD', '--', 'app', 'tools/release.sh');
  check(d.code === 0 && d.out.trim() === '', 'app/ y release.sh no cambiaron desde ese commit hasta HEAD (los binarios son de este código)', d.out);
  const dirty = git('status', '--porcelain', '--', 'app', 'tools/release.sh').out.trim();
  check(dirty === '', 'sin cambios sin commit en app/ ni release.sh', dirty);
}
// 2. huellas = las que se probaron
const sums = [...cand.matchAll(/^sha256 ([0-9a-f]{64}) \*(.+)$/gm)].map(m => [m[2].trim(), m[1]]);
check(sums.length === 4 && sums.every(([n, h]) => fs.existsSync(f(n)) && sha(f(n)) === h), 'las cuatro piezas son exactamente las compiladas y probadas (SHA-256 = candidato.txt)',
  sums.map(([n, h]) => `${n} ${h.slice(0, 12)} ahora ${fs.existsSync(f(n)) ? sha(f(n)).slice(0, 12) : '-'}`).join('\n'));

// 3. manifiesto
const m = JSON.parse(fs.readFileSync(f('vigia-manifest.json'), 'utf8'));
check(m.version === V && m.product === 'Vigia', `manifiesto de Vigía ${V}`, JSON.stringify(m));
check(m.package?.name === ZIP && m.package.size === fs.statSync(f(ZIP)).size && m.package.sha256?.toLowerCase() === sha(f(ZIP)), 'el manifiesto cuadra con el zip (nombre, tamaño, SHA-256)');
check(m.puente?.name === 'Puente.exe' && m.puente.size === fs.statSync(f('Puente.exe')).size && m.puente.sha256?.toLowerCase() === sha(f('Puente.exe')), 'el manifiesto cuadra con Puente.exe (tamaño, SHA-256)');
check(sha(f('Puente.exe')) === sha(f('Vigia-Setup.exe')), 'Vigia-Setup.exe es el mismo Puente.exe');

// 4. versión de Puente
const pvSrc = csprojVersion(fs.readFileSync(path.join(ROOT, 'app/Puente/Puente.csproj'), 'utf8'));
const pvBin = fileVersion(f('Puente.exe'));
check(vcmp(pvBin, pvSrc) === 0 && vcmp(m.puente?.version || '0', pvSrc) === 0, `Puente: binario ${pvBin}, Puente.csproj ${pvSrc}, manifiesto ${m.puente?.version}`);
const lastTag = git('describe', '--tags', '--abbrev=0', '--match', 'v[0-9]*').out.trim();
const pvLast = lastTag ? csprojVersion(git('show', `${lastTag}:app/Puente/Puente.csproj`).out) : null;
check(!pvLast || vcmp(pvSrc, pvLast) > 0, `Puente ${pvSrc} es mayor que el de ${lastTag || '-'} (${pvLast || '-'}): las instalaciones lo sustituyen de verdad`);
if (pvLast && vcmp(pvSrc, pvLast) === 0) check(git('diff', '--quiet', lastTag, 'HEAD', '--', 'app/Puente').code === 0, 'mismo número que la última publicada: su código tampoco cambió');

// 5. compilación pública
const tmp = fs.mkdtempSync(path.join(process.env.TMPDIR || os.tmpdir(), 'vigia-cand-'));
try {
  const pexe = path.join(tmp, 'Puente.exe'); fs.copyFileSync(f('Puente.exe'), pexe);   // se prueba una copia
  check(sh(pexe, ['--as-user', 'x']).code === 2 && sh(pexe, ['--bogus']).code === 2, 'Puente.exe es la compilación pública (sin comandos de prueba; un argumento desconocido no instala)');
  // 6. Vigía dentro del zip
  const x = path.join(tmp, 'zip'); fs.mkdirSync(x);
  const t = sh('C:/Windows/System32/tar.exe', ['-x', '-f', f(ZIP), '-C', x]);
  check(t.code === 0 && fs.existsSync(path.join(x, 'Vigia.exe')), 'el zip se abre y trae Vigia.exe', t.out);
  const vexe = path.join(x, 'Vigia.exe');
  check(vcmp(fileVersion(vexe), V) === 0, `Vigia.exe es la ${V} (${fileVersion(vexe)})`);
  const data = path.join(tmp, 'datos'); fs.mkdirSync(data);
  const env = { ...process.env, VIGIA_DATA: data };
  check(sh(vexe, ['--install-check'], { env }).code === 0, 'Vigia.exe --install-check');
  check(sh(vexe, ['--sensor-service'], { env }).code === 2, 'sin el servicio SYSTEM de sensores (--sensor-service es desconocido)');
  const dll = fs.readFileSync(path.join(x, 'Vigia.dll'));
  const has = s => dll.includes(Buffer.from(s, 'utf8')) || dll.includes(Buffer.from(s, 'utf16le'));
  check(has('LocationGuard') && has('Harden'), 'trae el arreglo de ubicación (LocationGuard.Harden)');
  check(!has('WlanGetNetworkBssList'), 'no llama a WlanGetNetworkBssList (redes vecinas y BSSID, que piden ubicación)');
  const pk = (/const string PublicKey = "([^"]+)"/.exec(fs.readFileSync(path.join(ROOT, 'app/Vigia/Updates.cs'), 'utf8')) || [])[1];
  const pkP = (/const string PublicKey = "([^"]+)"/.exec(fs.readFileSync(path.join(ROOT, 'app/Puente/Program.cs'), 'utf8')) || [])[1];
  check(!!pk && pk === pkP && has(pk), 'Vigía lleva la misma clave pública que Puente (la de confianza)');
  const next = s => (/NextKeys\s*=\s*\[([^\]]*)\]/.exec(s) || [])[1]?.trim();
  check(next(fs.readFileSync(path.join(ROOT, 'app/Puente/Program.cs'), 'utf8')) === '' && next(fs.readFileSync(path.join(ROOT, 'app/Vigia/Updates.cs'), 'utf8')) === '',
    'sin claves de relevo (NextKeys vacío en Puente y Vigía): ninguna clave de laboratorio se coló');
} finally { try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { } }

console.log(`\ncandidate-check: ${ok} OK, ${fail} FAIL`);
process.exit(fail ? 1 : 0);
