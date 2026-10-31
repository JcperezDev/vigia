#!/usr/bin/env node
// Pruebas de las herramientas de firma, SOLO con claves de prueba creadas aquí en una carpeta temporal.
// No lee, no copia y no nombra la clave de publicación.
//   node tools/signing/signing-test.js <Puente-test.exe>   (out/puente-test/Puente-test.exe, compilado con -p:PuenteTest=true)
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const repo = path.resolve(__dirname, '..', '..');
const PT = path.resolve(process.argv[2] || path.join(repo, 'out', 'puente-test', 'Puente-test.exe'));
if (!fs.existsSync(PT)) { console.error('Falta ' + PT); process.exit(2); }
const TMP = fs.mkdtempSync(path.join(process.env.TMPDIR || os.tmpdir(), 'vigia-firma-'));
const PS = path.join(process.env.SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
const ICACLS = path.join(process.env.SystemRoot, 'System32', 'icacls.exe');
let ok = 0, fail = 0;
function check(c, name, info) { if (c) { ok++; console.log('OK   ' + name); } else { fail++; console.log('FAIL ' + name + (info ? '\n     ' + String(info).trim().split('\n').slice(-6).join('\n     ') : '')); } }
function sh(exe, args, opt = {}) { const r = spawnSync(exe, args, { encoding: 'utf8', timeout: 120000, windowsHide: true, ...opt }); return { code: r.status, out: (r.stdout || '') + (r.stderr || '') }; }
function ps(file, args, opt) { return sh(PS, ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', file, ...args], opt); }
const relevo = path.join(__dirname, 'preparar-relevo.js');
const pubOf = () => crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).publicKey.export({ format: 'der', type: 'spki' }).toString('base64');

// ---------------------------------------------------------------- preparar-relevo.js
console.log('--- claves de confianza (preparar-relevo.js)');
check(sh('node', [relevo, 'comprobar', repo]).code === 0, 'el repositorio: Puente y Vigía confían en las mismas claves, todas válidas');
const R = path.join(TMP, 'repo');
for (const f of ['app/Puente/Program.cs', 'app/Vigia/Updates.cs', 'app/Puente/Puente.csproj']) { fs.mkdirSync(path.dirname(path.join(R, f)), { recursive: true }); fs.copyFileSync(path.join(repo, f), path.join(R, f)); }
const read = f => fs.readFileSync(path.join(R, f), 'utf8');
const cur = read('app/Puente/Program.cs').match(/const string PublicKey = "([^"]*)"/)[1];
const pver = () => read('app/Puente/Puente.csproj').match(/<Version>([^<]+)<\/Version>/)[1];
const pv0 = pver(), bump = (v, n) => v.replace(/\d+$/, x => String(Number(x) + n));
const B = pubOf();
let r = sh('node', [relevo, 'anadir', B, R]);
check(r.code === 0 && read('app/Puente/Program.cs').includes(`NextKeys = ["${B}"]`) && read('app/Vigia/Updates.cs').includes(`NextKeys = ["${B}"]`)
  && read('app/Puente/Program.cs').includes(`PublicKey = "${cur}"`), 'versión puente: la clave nueva entra en NextKeys de Puente y de Vigía; la actual sigue', r.out);
check(pver() === bump(pv0, 1), `versión puente: Puente sube de ${pv0} a ${bump(pv0, 1)}, así las instalaciones cambian su Puente y reciben la clave nueva`, pver());
const snap = read('app/Puente/Program.cs');
r = sh('node', [relevo, 'anadir', B, R]);
check(r.code === 0 && read('app/Puente/Program.cs') === snap, 'añadir la misma clave dos veces no cambia nada', r.out);
r = sh('node', [relevo, 'anadir', 'esto-no-es-una-clave', R]);
check(r.code === 1 && read('app/Puente/Program.cs') === snap, 'una clave mal pegada se rechaza sin tocar nada', r.out);
r = sh('node', [relevo, 'anadir', cur, R]);
check(r.code === 1 && read('app/Puente/Program.cs') === snap, 'la clave actual no puede ser su propio relevo', r.out);
const priv = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).privateKey.export({ format: 'pem', type: 'pkcs8' });
r = sh('node', [relevo, 'anadir', priv, R]);
check(r.code === 1 && read('app/Puente/Program.cs') === snap, 'una clave privada pegada por error se rechaza', r.out.replace(/-----BEGIN[\s\S]*END[^-]*-----/g, '[clave de prueba]'));
const rsa = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 }).publicKey.export({ format: 'der', type: 'spki' }).toString('base64');
r = sh('node', [relevo, 'anadir', rsa, R]);
check(r.code === 1, 'una clave pública que no es P-256 (RSA) se rechaza', r.out);
check(pver() === bump(pv0, 1), 'lo rechazado y lo repetido no vuelven a subir la versión de Puente', pver());
const C = pubOf();
r = sh('node', [relevo, 'anadir', C, R]);
r = sh('node', [relevo, 'promover', R]);
check(r.code === 1 && /exactamente una/.test(r.out), 'con dos claves de relevo no se promueve (hay que elegir)', r.out);
fs.writeFileSync(path.join(R, 'app/Puente/Program.cs'), snap); fs.writeFileSync(path.join(R, 'app/Vigia/Updates.cs'), read('app/Vigia/Updates.cs').replace(`, "${C}"`, ''));
r = sh('node', [relevo, 'comprobar', R]);
check(r.code === 0, 'vuelve al estado de versión puente (una clave de relevo)', r.out);
r = sh('node', [relevo, 'promover', R]);
check(r.code === 0 && read('app/Puente/Program.cs').includes(`PublicKey = "${B}"`) && read('app/Vigia/Updates.cs').includes(`PublicKey = "${B}"`)
  && read('app/Puente/Program.cs').includes('NextKeys = []') && !read('app/Vigia/Updates.cs').includes(cur),
  'versión siguiente: la nueva pasa a ser la única de confianza y la vieja deja de valer', r.out);
check(pver() === bump(pv0, 3), 'versión siguiente: Puente vuelve a subir (cada cambio de claves trae un Puente más nuevo)', pver());
fs.writeFileSync(path.join(R, 'app/Vigia/Updates.cs'), read('app/Vigia/Updates.cs').replace('NextKeys = []', `NextKeys = ["${C}"]`));
r = sh('node', [relevo, 'comprobar', R]);
check(r.code === 1 && /distinta/.test(r.out), 'si Puente y Vigía no confían en las mismas claves, comprobar falla', r.out);
r = sh('node', [relevo, 'anadir', pubOf(), R]);
check(r.code === 1, 'con las listas descuadradas no se añade nada encima', r.out);

// ---------------------------------------------------------------- quitar-acceso-sandbox.ps1
console.log('--- permisos de la carpeta de la clave (quitar-acceso-sandbox.ps1)');
const qa = path.join(__dirname, 'quitar-acceso-sandbox.ps1');
const KD = path.join(TMP, 'perfil', '.vigia');
fs.mkdirSync(KD, { recursive: true });
const keyA = path.join(TMP, 'claves', 'a.pem');
r = sh(PT, ['--genkey', keyA]); const pubA = r.out.trim().split(/\r?\n/).pop();
check(r.code === 0, 'clave de prueba creada (solo para esta prueba)', r.out);
// el caso real: la clave hereda los permisos de su carpeta, y la carpeta da lectura a un grupo extra
const grupo = 'CodexSandboxUsers';
const hay = sh(PS, ['-NoProfile', '-Command', `try{(New-Object Security.Principal.NTAccount('${grupo}')).Translate([Security.Principal.SecurityIdentifier]).Value}catch{''}`]).out.trim();
const G = hay ? grupo : 'Users';   // en un PC sin ese grupo se prueba con Usuarios (S-1-5-32-545)
sh(ICACLS, [KD, '/grant', `${hay ? grupo : '*S-1-5-32-545'}:(OI)(CI)RX`]);
const kin = path.join(KD, 'release-key.pem');
fs.writeFileSync(kin, fs.readFileSync(keyA));   // archivo nuevo: hereda de la carpeta, como la clave de verdad
r = sh(PT, ['--key-check', kin]);
check(r.code === 3, `antes: la clave hereda lectura para ${G} y sale NO protegida`, r.out);
const copia = path.join(TMP, 'perfil', 'acl-antes.acl');
r = ps(qa, ['-Carpeta', KD, '-Grupo', G, '-Puente', PT, '-Copia', copia]);
check(r.code === 0 && fs.existsSync(copia), 'el script guarda la copia de permisos, quita el grupo y la clave sale protegida (rc 0)', r.out);
r = sh(PT, ['--key-check', kin]);
check(r.code === 0, 'después: --key-check da «Clave protegida»', r.out);
const aclNow = sh(ICACLS, [KD]).out;
check(!new RegExp(G, 'i').test(aclNow), `después: ${G} ya no aparece en la carpeta`, aclNow);
check(fs.readFileSync(kin).equals(fs.readFileSync(keyA)), 'la clave no cambió (mismos bytes): solo se tocaron permisos');
r = ps(qa, ['-Carpeta', KD, '-Grupo', G, '-Puente', PT, '-Copia', copia]);
check(r.code === 1 && /no se pisa/.test(r.out), 'no pisa una copia de permisos que ya existe', r.out);
r = ps(qa, ['-Carpeta', KD, '-Deshacer', copia]);
const back = sh(PT, ['--key-check', kin]);
check(r.code === 0 && back.code === 3, 'deshacer devuelve los permisos de antes (la clave vuelve a salir NO protegida)', r.out + back.out);
// herencia desde más arriba (como el perfil): después del script, un permiso nuevo arriba ya no llega a la clave
const P2 = path.join(TMP, 'perfil2'), KD2 = path.join(P2, '.vigia'); fs.mkdirSync(KD2, { recursive: true });
fs.writeFileSync(path.join(KD2, 'release-key.pem'), fs.readFileSync(keyA));
r = ps(qa, ['-Carpeta', KD2, '-Grupo', G, '-Puente', PT, '-Copia', path.join(P2, 'c.acl')]);
check(r.code === 0, 'permiso heredado de más arriba: el script corta la herencia y la clave sale protegida', r.out);
sh(ICACLS, [P2, '/grant', '*S-1-5-32-545:(OI)(CI)R']);
r = sh(PT, ['--key-check', path.join(KD2, 'release-key.pem')]);
check(r.code === 0, 'después, dar lectura a Usuarios en la carpeta de arriba ya no llega a la clave', r.out);
r = ps(qa, ['-Carpeta', path.join(TMP, 'no-existe')]);
check(r.code === 1, 'carpeta que no existe: no hace nada (rc 1)', r.out);

// ---------------------------------------------------------------- firmar-fuera.ps1
console.log('--- firma en el equipo de firma (firmar-fuera.ps1)');
const ff = path.join(__dirname, 'firmar-fuera.ps1');
const V = path.join(TMP, 'entrega', '0.99.0');
fs.mkdirSync(V, { recursive: true });
const tarExe = path.join(process.env.SystemRoot, 'System32', 'tar.exe');
const zsrc = path.join(TMP, 'zsrc'); fs.mkdirSync(zsrc); fs.writeFileSync(path.join(zsrc, 'Vigia.exe'), 'prueba');
sh(tarExe, ['-a', '-c', '-f', path.join(V, 'Vigia-0.99.0-win-x64.zip'), '-C', zsrc, 'Vigia.exe']);
fs.copyFileSync(PT, path.join(V, 'Puente.exe')); fs.copyFileSync(PT, path.join(V, 'Vigia-Setup.exe'));
const env = { env: { ...process.env, PUENTE_TEST_PUBKEY: pubA } };
r = sh(PT, ['--manifest', V, '0.99.0'], env);
check(r.code === 0 && fs.existsSync(path.join(V, 'vigia-manifest.json')), 'carpeta de versión sin firmar, como la deja VIGIA_RELEASE_SIGN=fuera', r.out);
const dp = path.join(TMP, 'estacion', 'clave.dpapi');
fs.mkdirSync(path.dirname(dp));
r = sh(PT, ['--protect-key', keyA, dp]);
check(r.code === 0, 'clave de prueba cifrada con DPAPI de esta cuenta (como en el equipo de firma)', r.out);
{ // --protect-key: lo que queda en disco no es la clave, solo la abre esta cuenta, y no se pisa ni se acepta alterado
  const blob = fs.readFileSync(dp);
  check(!/BEGIN|PRIVATE/.test(blob.toString('latin1')) && !blob.includes(fs.readFileSync(keyA).subarray(40, 90)), '--protect-key: el .dpapi no contiene la clave en claro', `${blob.length} bytes`);
  const acl = sh(PS, ['-NoProfile', '-Command', `$a = Get-Acl -LiteralPath '${dp.replace(/'/g, "''")}'; '' + $a.AreAccessRulesProtected + '|' + (@($a.Access | ForEach-Object { '' + $_.IdentityReference + ':' + $_.AccessControlType + ':' + $_.IsInherited }) -join ';')`]).out.trim();
  const me = sh(PS, ['-NoProfile', '-Command', '[Security.Principal.WindowsIdentity]::GetCurrent().Name']).out.trim();
  check(acl === `True|${me}:Allow:False`, '--protect-key: el .dpapi solo lo puede abrir tu cuenta (sin herencia, una sola entrada)', acl);
  r = sh(PT, ['--protect-key', keyA, dp]);
  check(r.code === 1 && fs.readFileSync(dp).equals(blob), '--protect-key no sobrescribe un .dpapi que ya existe', r.out);
  const bad = path.join(TMP, 'estacion', 'alterada.dpapi'); const b2 = Buffer.from(blob); b2[b2.length >> 1] ^= 0xff; fs.writeFileSync(bad, b2);
  sh(ICACLS, [bad, '/inheritance:r', '/grant:r', `${me}:F`]);
  const tf = path.join(TMP, 'estacion', 'nota.txt'); fs.writeFileSync(tf, 'prueba');
  r = sh(PT, ['--sign', tf, bad]);
  check(r.code !== 0 && !fs.existsSync(tf + '.sig'), 'un .dpapi alterado no descifra y no firma nada', r.out);
  r = sh(PT, ['--sign', tf, dp]);
  check(r.code === 0 && sh(PT, ['--verify', tf], env).code === 0, 'con el .dpapi bueno firma y la firma pasa con la clave pública de prueba', r.out);
  fs.rmSync(tf + '.sig', { force: true });
}
const huella = crypto.createHash('sha256').update(fs.readFileSync(PT)).digest('hex').toUpperCase();
const sigs = () => fs.readdirSync(V).filter(f => f.endsWith('.sig'));
r = ps(ff, ['-Version', V, '-Clave', dp, '-Puente', PT, '-HuellaPuente', '00' + huella.slice(2), '-PermitirRed'], env);
check(r.code === 4 && sigs().length === 0, 'un Puente.exe distinto del anotado: no firma nada (rc 4)', r.out);
r = ps(ff, ['-Version', V, '-Clave', dp, '-Puente', PT, '-HuellaPuente', huella], env);
const red = sh(PS, ['-NoProfile', '-Command', "@(Get-NetAdapter -EA SilentlyContinue | ? Status -eq 'Up').Count"]).out.trim();
check(red === '0' ? r.code === 0 : (r.code === 5 && sigs().length === 0), red === '0' ? 'sin red: firma' : 'con red activa: no firma sin -PermitirRed (rc 5)', r.out);
const kopen = path.join(TMP, 'estacion', 'abierta.pem'); fs.copyFileSync(keyA, kopen); sh(ICACLS, [kopen, '/grant', '*S-1-5-32-545:R']);
r = ps(ff, ['-Version', V, '-Clave', kopen, '-Puente', PT, '-HuellaPuente', huella, '-PermitirRed'], env);
check(r.code === 3 && sigs().length === 0, 'una clave que otro grupo puede leer: no firma (rc 3)', r.out);
const kin2 = path.join(V, 'clave.pem'); fs.copyFileSync(keyA, kin2);
r = ps(ff, ['-Version', V, '-Clave', kin2, '-Puente', PT, '-HuellaPuente', huella, '-PermitirRed'], env);
check(r.code === 1 && sigs().length === 0, 'la clave dentro de la carpeta que vuelve al PC de desarrollo: no firma', r.out);
fs.rmSync(kin2);
const keyB = path.join(TMP, 'claves', 'b.pem'); sh(PT, ['--genkey', keyB]);
r = ps(ff, ['-Version', V, '-Clave', keyB, '-Puente', PT, '-HuellaPuente', huella, '-PermitirRed'], env);
check(r.code === 1 && sigs().length === 0, 'una clave que no es la de confianza de ese Puente: lo firmado no cuadra y se borra', r.out);
r = ps(ff, ['-Version', V, '-Clave', dp, '-Puente', PT, '-HuellaPuente', huella, '-PermitirRed'], env);
check(r.code === 0 && sigs().length === 4 && fs.existsSync(path.join(V, 'firmas-0.99.0.txt')), 'con todo en orden: las cuatro firmas y el archivo de huellas', r.out);
const txt = fs.readFileSync(path.join(V, 'firmas-0.99.0.txt'), 'utf8');
check((txt.match(/^[0-9a-f]{64}  /gim) || []).length === 4 && !/PRIVATE|BEGIN/.test(txt), 'el archivo de huellas trae 4 SHA-256 y nada de la clave');
for (const f of ['Vigia-0.99.0-win-x64.zip', 'Puente.exe', 'Vigia-Setup.exe']) {
  r = sh(PT, ['--verify', path.join(V, f)], env);
  if (r.code !== 0) break;
}
check(r.code === 0 && sh(PT, ['--verify-manifest', path.join(V, 'vigia-manifest.json')], env).code === 0, 'lo firmado pasa la comprobación de Puente con la clave pública de prueba');
// relevo: un Puente que solo confía en la clave de prueba B rechaza lo firmado con A; uno que confía en A y B lo acepta
const pubB = crypto.createPublicKey(fs.readFileSync(keyB)).export({ format: 'der', type: 'spki' }).toString('base64');
check(sh(PT, ['--verify', path.join(V, 'Puente.exe')], { env: { ...process.env, PUENTE_TEST_PUBKEY: pubB } }).code !== 0,
  'después de rotar (solo la clave nueva de confianza), lo firmado con la vieja ya no pasa');
check(sh(PT, ['--verify', path.join(V, 'Puente.exe')], { env: { ...process.env, PUENTE_TEST_PUBKEY: pubB + ';' + pubA } }).code === 0,
  'durante el relevo (vieja y nueva de confianza), lo firmado con la vieja sigue pasando');
r = ps(ff, ['-Version', V, '-Clave', dp, '-Puente', PT, '-HuellaPuente', huella, '-PermitirRed'], env);
check(r.code === 1 && sigs().length === 4, 'no pisa firmas que ya están', r.out);

// ---------------------------------------------------------------- limpieza (solo la carpeta temporal de esta prueba)
try { fs.rmSync(TMP, { recursive: true, force: true }); } catch { }
console.log(`signing-test: OK=${ok} FAIL=${fail}`);
process.exit(fail ? 1 : 0);
