#!/usr/bin/env node
// Pruebas del equipo de firma (VigiaFirma + firmar-fuera.ps1 + preparar-relevo.js) SOLO con claves de laboratorio
// creadas en una carpeta temporal y borradas al terminar. Nunca toca ~/.vigia ni ninguna clave real.
//
//   node tools/signing/estacion-test.js <VigiaFirma.exe> <Puente-test.exe> <carpeta-candidato>
//
// <Puente-test.exe>: compilación de pruebas de Puente (PuenteTest=true), cuya lista de confianza sale de
// PUENTE_TEST_PUBKEY. <carpeta-candidato>: out/<versión> con las cuatro piezas sin firmar (se copian; no se tocan).
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');
const relevo = require('./preparar-relevo.js');

const [FIRMA, PUENTE, CAND] = process.argv.slice(2).map(p => p && path.resolve(p));
if (!FIRMA || !PUENTE || !CAND) { console.error('Uso: estacion-test.js <VigiaFirma.exe> <Puente-test.exe> <carpeta-candidato>'); process.exit(2); }
const ROOT = path.resolve(__dirname, '..', '..');
const PS1 = path.join(__dirname, 'firmar-fuera.ps1');
const HOME_VIGIA = path.join(os.homedir(), '.vigia').toLowerCase();

let ok = 0, fail = 0;
const check = (c, name, extra) => { if (c) { ok++; console.log('OK   ' + name); } else { fail++; console.log('FAIL ' + name + (extra ? '\n     ' + String(extra).slice(-900).replace(/\n/g, '\n     ') : '')); } };
const tmp = fs.mkdtempSync(path.join(process.env.TMPDIR || os.tmpdir(), 'estacion-test-'));
if (tmp.toLowerCase().startsWith(HOME_VIGIA)) throw new Error('carpeta temporal dentro de ~/.vigia');
const run = (exe, args, { input = '', env = {} } = {}) => {
  const r = spawnSync(exe, args, { input, env: { ...process.env, ...env }, encoding: 'utf8', windowsHide: true, timeout: 180000 });
  return { code: r.status, out: (r.stdout || '') + (r.stderr || '') };
};
const vf = (args, input, env) => run(FIRMA, args, { input, env });
const ps = (args, env) => run('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', PS1, ...args], { env });
const sha = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const icacls = f => run('icacls.exe', [f]).out;
const ownOnly = f => {
  const t = icacls(f), me = (process.env.USERDOMAIN ? process.env.USERDOMAIN + '\\' : '') + os.userInfo().username;
  const aces = t.split(/\r?\n/).map(l => l.replace(f, '').trim()).filter(l => /:\(/.test(l));
  return aces.length === 1 && aces[0].toLowerCase().startsWith(me.toLowerCase() + ':') && !/\(I\)/.test(aces[0]);
};
const F1 = 'frase de laboratorio 1234567890', F2 = 'otra frase de laboratorio 0987654321';
const pubOf = file => relevo.readPublica(file).pub;

try {
  const K = path.join(tmp, 'claves'); fs.mkdirSync(K);
  // ---------------------------------------------------------------- clave nueva
  console.log('--- VigiaFirma nueva / respaldo / restaurar');
  let r = vf(['nueva', K, 'labB'], `${F1}\n${F1}\n`);
  const bD = path.join(K, 'vigia-labB.dpapi'), bR = path.join(K, 'vigia-labB.respaldo.pem'), bP = path.join(K, 'vigia-labB.publica.txt');
  check(r.code === 0 && [bD, bR, bP].every(f => fs.existsSync(f)), 'nueva: deja .dpapi, .respaldo.pem y .publica.txt', r.out);
  check(fs.readdirSync(K).length === 3, 'nueva: no deja ningún otro archivo (nada en claro)', fs.readdirSync(K).join(', '));
  const dpapiTxt = fs.readFileSync(bD).toString('latin1');
  check(!/PRIVATE|BEGIN/.test(dpapiTxt) && !/PRIVATE|BEGIN/.test(fs.readFileSync(bD).toString('utf16le')), 'el .dpapi no contiene ningún bloque PEM legible');
  const respTxt = fs.readFileSync(bR, 'utf8');
  check(/-----BEGIN ENCRYPTED PRIVATE KEY-----/.test(respTxt) && !/-----BEGIN PRIVATE KEY-----/.test(respTxt), 'el respaldo está cifrado (ENCRYPTED PRIVATE KEY, no PRIVATE KEY en claro)');
  let pubB = null; try { pubB = pubOf(bP); } catch (e) { r.out = e.message; }
  check(!!pubB && !/PRIVATE|BEGIN/.test(fs.readFileSync(bP, 'utf8')), 'el .publica.txt solo trae la pública, con huella correcta', r.out);
  check(!r.out.includes(respTxt.split('\n')[1] || '#') && !/PRIVATE KEY/.test(r.out), 'la salida no muestra la clave privada ni el respaldo');
  check(!r.out.includes(F1), 'la salida no muestra la frase');
  check(ownOnly(bD) && ownOnly(bR), 'el .dpapi y el respaldo: solo tu cuenta, sin herencia', icacls(bD) + icacls(bR));
  const before = fs.readdirSync(K).map(f => sha(path.join(K, f))).join();
  r = vf(['nueva', K, 'labB'], `${F1}\n${F1}\n`);
  check(r.code === 1 && fs.readdirSync(K).map(f => sha(path.join(K, f))).join() === before, 'nueva: no sobrescribe una clave que ya existe', r.out);
  const K2 = path.join(tmp, 'k2');
  r = vf(['nueva', K2, 'x'], 'corta\ncorta\n');
  check(r.code === 1 && (!fs.existsSync(K2) || fs.readdirSync(K2).length === 0), 'nueva: frase de menos de 16 caracteres -> no crea nada', r.out);
  r = vf(['nueva', K2, 'x'], `${F1}\n${F2}\n`);
  check(r.code === 1 && (!fs.existsSync(K2) || fs.readdirSync(K2).length === 0), 'nueva: las dos frases no coinciden -> no crea nada', r.out);
  r = vf(['nueva', K2, '../fuera'], `${F1}\n${F1}\n`);
  check(r.code === 2, 'nueva: nombre con ruta -> se niega', r.out);

  r = vf(['comprobar-respaldo', bR, bP], `${F1}\n`);
  check(r.code === 0, 'comprobar-respaldo: abre con la frase y es la clave labB', r.out);
  r = vf(['comprobar-respaldo', bR, bP], `${F2}\n`);
  check(r.code === 1, 'comprobar-respaldo: con otra frase falla', r.out);

  r = vf(['nueva', K, 'labA'], `${F2}\n${F2}\n`);
  const aD = path.join(K, 'vigia-labA.dpapi'), aP = path.join(K, 'vigia-labA.publica.txt');
  check(r.code === 0, 'nueva: segunda clave de laboratorio (labA)', r.out);
  r = vf(['comprobar-respaldo', bR, aP], `${F1}\n`);
  check(r.code === 1, 'comprobar-respaldo: el respaldo de labB no pasa por labA', r.out);

  const rest = path.join(tmp, 'restaurada.dpapi');
  r = vf(['restaurar', bR, rest], `${F1}\n`);
  const r2 = vf(['publica', rest]);
  check(r.code === 0 && r2.code === 0 && r2.out.includes(pubB) && ownOnly(rest), 'restaurar: el respaldo devuelve la misma clave (solo tu cuenta)', r.out + r2.out);
  r = vf(['restaurar', bR, rest], `${F1}\n`);
  check(r.code === 1, 'restaurar: no pisa un archivo que ya existe', r.out);

  // ---------------------------------------------------------------- importar (cómo se pasa A al equipo de firma)
  console.log('--- VigiaFirma importar (clave en claro de laboratorio)');
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const pemC = path.join(tmp, 'labC-en-claro.pem');
  fs.writeFileSync(pemC, privateKey.export({ format: 'pem', type: 'pkcs8' }));
  const pubC = Buffer.from(publicKey.export({ format: 'der', type: 'spki' })).toString('base64');
  const expC = path.join(tmp, 'esperada-labC.publica.txt');
  fs.writeFileSync(expC, `vigia-clave-publica 1\nnombre labC\npublica ${pubC}\nhuella ${relevo.huella(pubC)}\n`);
  const shaPem = sha(pemC), K3 = path.join(tmp, 'k3');
  r = vf(['importar', pemC, K3, 'labC', aP], `${F1}\n${F1}\n`);
  check(r.code === 1 && (!fs.existsSync(K3) || fs.readdirSync(K3).length === 0), 'importar: si la clave no es la esperada, no importa nada', r.out);
  r = vf(['importar', pemC, K3, 'labC', expC], `${F1}\n${F1}\n`);
  check(r.code === 0 && pubOf(path.join(K3, 'vigia-labC.publica.txt')) === pubC, 'importar: la clave esperada pasa a .dpapi + respaldo cifrado', r.out);
  check(sha(pemC) === shaPem && fs.existsSync(pemC), 'importar: el .pem original no se cambia, ni se mueve, ni se borra (eso lo decide el dueño)');

  // ---------------------------------------------------------------- versión de laboratorio
  const zipName = fs.readdirSync(CAND).find(f => /^Vigia-.*-win-x64\.zip$/.test(f));
  const PIEZAS = [zipName, 'Puente.exe', 'Vigia-Setup.exe', 'vigia-manifest.json'];
  const copia = name => {
    const d = path.join(tmp, name); fs.mkdirSync(d);
    for (const p of PIEZAS) fs.copyFileSync(path.join(CAND, p), path.join(d, p));
    return d;
  };
  const hoja = (dir, file) => { fs.writeFileSync(file, PIEZAS.map(p => `sha256 ${sha(path.join(dir, p))} *${p}`).join('\n') + '\n'); return file; };
  const pubA = pubOf(aP);
  const envAB = { PUENTE_TEST_PUBKEY: `${pubA};${pubB}` }, envA = { PUENTE_TEST_PUBKEY: pubA };
  const sigs = d => PIEZAS.filter(p => fs.existsSync(path.join(d, p + '.sig'))).length;
  const hp = sha(PUENTE).toUpperCase();

  console.log('--- Puente --sign-release con una clave del equipo de firma + VigiaFirma verificar');
  const v1 = copia('v1'), h1 = hoja(v1, path.join(tmp, 'huellas-v1.txt'));
  r = run(PUENTE, ['--sign-release', v1, aD], { env: envAB });
  check(r.code === 0 && sigs(v1) === 4, 'Puente firma con el .dpapi que hizo VigiaFirma (misma protección DPAPI)', r.out);
  r = vf(['verificar', v1, aP, h1]);
  check(r.code === 0, 'verificar: firmas, manifiesto y huellas con la clave labA', r.out);
  r = vf(['verificar', v1, bP, h1]);
  check(r.code === 1, 'verificar: con otra clave pública (labB) no pasa', r.out);
  const man = path.join(v1, 'vigia-manifest.json'), manOrig = fs.readFileSync(man);
  fs.writeFileSync(man, Buffer.concat([manOrig, Buffer.from(' ')]));
  r = vf(['verificar', v1, aP]);
  check(r.code === 1, 'verificar: manifiesto cambiado después de firmar -> no pasa', r.out);
  fs.writeFileSync(man, manOrig);
  const hMal = path.join(tmp, 'huellas-mal.txt');
  fs.writeFileSync(hMal, fs.readFileSync(h1, 'utf8').replace(/sha256 ([0-9a-f])/, (m, c) => 'sha256 ' + (c === '0' ? '1' : '0')));
  r = vf(['verificar', v1, aP, hMal]);
  check(r.code === 1, 'verificar: firmado bien pero distinto de la hoja de huellas (manifiesto sustituido) -> no pasa', r.out);
  const sPu = path.join(v1, 'Puente.exe.sig'), sigPu = fs.readFileSync(sPu, 'utf8');
  fs.writeFileSync(sPu, fs.readFileSync(path.join(v1, zipName + '.sig'), 'utf8'));
  r = vf(['verificar', v1, aP]);
  check(r.code === 1, 'verificar: una firma cambiada por la de otra pieza -> no pasa', r.out);
  fs.writeFileSync(sPu, sigPu);

  console.log('--- firmar-fuera.ps1');
  const net = run('powershell.exe', ['-NoProfile', '-Command', "@(Get-NetAdapter -EA SilentlyContinue | ? Status -eq 'Up').Count"]).out.trim();
  const red = Number(net) > 0;
  const base = (dir, huellas, extra = []) => ['-Version', dir, '-Clave', aD, '-Publica', aP, '-Huellas', huellas, '-Puente', PUENTE, '-HuellaPuente', hp, '-Firma', FIRMA, ...extra];
  const v2 = copia('v2'), h2 = hoja(v2, path.join(tmp, 'huellas-v2.txt'));
  if (red) {
    r = ps(base(v2, h2), envAB);
    check(r.code === 5 && sigs(v2) === 0, 'con red activa y sin -PermitirRed no firma (rc 5)', r.out);
  } else console.log('-    (este equipo no tiene red activa: el caso rc 5 no se puede probar aquí)');
  const PR = red ? ['-PermitirRed'] : [];
  r = ps(base(v2, path.join(v2, 'huellas-dentro.txt'), PR), envAB);
  check(r.code !== 0 && sigs(v2) === 0, 'la hoja de huellas dentro de la carpeta de la versión -> no firma', r.out);
  fs.copyFileSync(h2, path.join(v2, 'huellas-dentro.txt'));
  r = ps(base(v2, path.join(v2, 'huellas-dentro.txt'), PR), envAB);
  check(r.code === 4 && sigs(v2) === 0, '...aunque la hoja dentro sea correcta (rc 4)', r.out);
  fs.rmSync(path.join(v2, 'huellas-dentro.txt'));
  fs.appendFileSync(path.join(v2, 'vigia-manifest.json'), ' ');
  r = ps(base(v2, h2, PR), envAB);
  check(r.code === 4 && sigs(v2) === 0, 'una pieza distinta de la hoja (manifiesto cambiado) -> no firma (rc 4)', r.out);
  fs.copyFileSync(path.join(CAND, 'vigia-manifest.json'), path.join(v2, 'vigia-manifest.json'));
  r = ps([...base(v2, h2).slice(0, 10), '-HuellaPuente', '0'.repeat(64), '-Firma', FIRMA, ...PR], envAB);
  check(r.code === 4 && sigs(v2) === 0, 'un Puente que no es el anotado -> no firma (rc 4)', r.out);
  r = ps([...base(v2, h2, PR).map(x => x === aP ? bP : x)], envAB);
  check(r.code === 6 && sigs(v2) === 0, 'firmado con labA pero exigido labB (lo que verificarán las instalaciones) -> firmas quitadas (rc 6)', r.out);
  r = ps(base(v2, h2, [...PR, '-ClaveRelevo', bD]), envA);
  check(r.code === 7 && sigs(v2) === 0, 'transición: el Puente de la versión NO confía en la clave nueva -> no firma (rc 7)', r.out);
  r = ps(base(v2, h2, [...PR, '-ClaveRelevo', bD]), envAB);
  const txt = path.join(v2, `${path.basename(v2)}`.replace(/^/, 'firmas-') + '.txt');
  check(r.code === 0 && sigs(v2) === 4 && fs.existsSync(txt), 'transición bien: Puente confía en A y B, firma con A, comprobado con labA (rc 0)', r.out);
  check(vf(['verificar', v2, aP, h2]).code === 0, '...y lo firmado pasa verificar con labA y la hoja');
  check(!fs.readdirSync(v2).some(f => /\.(dpapi|pem|key|pfx)$/i.test(f)) && !fs.readdirSync(v2).some(f => /PRIVATE KEY/.test(fs.readFileSync(path.join(v2, f)).toString('latin1').slice(0, 1 << 20)) && !/\.zip$|\.exe$/.test(f)),
    'a la carpeta de la versión solo van .sig y firmas-*.txt (ninguna clave)', fs.readdirSync(v2).join(', '));
  const lastLines = r.out;
  check(!/BEGIN|PRIVATE KEY/.test(lastLines), 'la salida de firmar-fuera no muestra ninguna clave');
  const v3 = copia('v3'), h3 = hoja(v3, path.join(tmp, 'huellas-v3.txt'));
  r = ps([...base(v3, h3, PR).map(x => x === aD ? bD : x === aP ? bP : x)], { PUENTE_TEST_PUBKEY: pubB });
  check(r.code === 0 && vf(['verificar', v3, bP, h3]).code === 0 && vf(['verificar', v3, aP, h3]).code === 1,
    'versión posterior firmada con labB: pasa con labB y no con labA', r.out);

  // ---------------------------------------------------------------- preparar-relevo con el .publica.txt
  console.log('--- preparar-relevo.js con vigia-<nombre>.publica.txt');
  const R = path.join(tmp, 'raiz');
  for (const f of ['app/Puente/Program.cs', 'app/Vigia/Updates.cs', 'app/Puente/Puente.csproj']) {
    fs.mkdirSync(path.dirname(path.join(R, f)), { recursive: true }); fs.copyFileSync(path.join(ROOT, f), path.join(R, f));
  }
  const ver0 = /<Version>([^<]+)</.exec(fs.readFileSync(path.join(R, 'app/Puente/Puente.csproj'), 'utf8'))[1];
  const bad = path.join(tmp, 'vigia-mal.publica.txt');
  fs.writeFileSync(bad, fs.readFileSync(bP, 'utf8').replace(/huella ([0-9a-f])/, (m, c) => 'huella ' + (c === '0' ? '1' : '0')));
  const snap = () => ['app/Puente/Program.cs', 'app/Vigia/Updates.cs', 'app/Puente/Puente.csproj'].map(f => sha(path.join(R, f))).join();
  let s0 = snap();
  check(relevo.main(['anadir', bad, R]) === 1 && snap() === s0, 'anadir: huella que no es la de la clave -> no cambia nada');
  const priv = path.join(tmp, 'vigia-priv.publica.txt'); fs.writeFileSync(priv, fs.readFileSync(bP, 'utf8') + '-----BEGIN PRIVATE KEY-----\n');
  check(relevo.main(['anadir', priv, R]) === 1 && snap() === s0, 'anadir: un archivo con bloque de clave privada -> se niega');
  check(relevo.main(['anadir', bP, R]) === 0, 'anadir con vigia-labB.publica.txt');
  const st = ['app/Puente/Program.cs', 'app/Vigia/Updates.cs'].map(f => fs.readFileSync(path.join(R, f), 'utf8'));
  check(st.every(s => s.includes(`NextKeys = ["${pubB}"]`)), 'labB queda como clave de relevo en Puente y en Vigía');
  const ver1 = /<Version>([^<]+)</.exec(fs.readFileSync(path.join(R, 'app/Puente/Puente.csproj'), 'utf8'))[1];
  check(ver1 !== ver0 && ver1.split('.').map(Number)[2] === ver0.split('.').map(Number)[2] + 1, `Puente sube de número (${ver0} -> ${ver1})`);
  const ap = relevo.approvedKeys(R);
  check(ap.length === 1 && ap[0].pub === pubB && ap[0].nombre === 'labB', 'la pública queda registrada en tools/signing/claves-publicas (candidate-check la acepta)');
  check(relevo.main(['comprobar', R]) === 0, 'comprobar: listas iguales y válidas');
  s0 = snap();
  check(relevo.main(['anadir', bP, R]) === 0 && snap() === s0, 'anadir otra vez la misma: no cambia nada (ni sube Puente)');
  check(relevo.main(['promover', R]) === 0 && ['app/Puente/Program.cs', 'app/Vigia/Updates.cs'].every(f => {
    const s = fs.readFileSync(path.join(R, f), 'utf8'); return s.includes(`PublicKey = "${pubB}"`) && s.includes('NextKeys = []');
  }), 'promover: labB pasa a ser la única de confianza (A retirada)');
} catch (e) {
  fail++; console.log('FAIL excepción: ' + (e.stack || e));
} finally {
  try { fs.rmSync(tmp, { recursive: true, force: true }); } catch { }
  check(!fs.existsSync(tmp), 'la carpeta temporal con las claves de laboratorio se borró');
}
console.log(`\nestacion-test: ${ok} OK, ${fail} FAIL`);
process.exit(fail ? 1 : 0);
