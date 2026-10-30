// Pruebas de instalación, actualización y migración de Puente, sin tocar el PC de verdad.
//
//   node tools/puente-test.js
//
// Usa la compilación de pruebas (out/puente-test/Puente-test.exe, con -p:PuenteTest=true), que solo instala en
// carpetas %TEMP%\vigia-test-xxxxxxxx, escribe el registro en HKCU\Software\Vigia-test\<carpeta>, no toca el
// Programador de tareas ni pide UAC, y acepta una clave pública de prueba. Los paquetes son un Vigía de mentira
// (tools/puente-test/fake-vigia) firmado con una clave hecha aquí mismo en %TEMP%; la clave de publicar no se usa.
// Al terminar se borra todo lo que se creó. Sale con 1 si algo falla.
'use strict';
const { spawnSync, spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const repo = path.resolve(__dirname, '..');
const PT = process.env.PUENTE_TEST_EXE || path.join(repo, 'out', 'puente-test', 'Puente-test.exe');   // PUENTE_TEST_EXE: probar otro Puente (p. ej. uno anterior)
const PUBLIC_PUENTE = path.join(repo, 'app', 'Puente', 'bin', 'Release', 'net10.0', 'win-x64', 'Puente.exe');
const TEMP = fs.realpathSync.native(process.env.TEMP);   // ruta larga (sin PICKLE~1): así la escribe Puente
const DOTNET = path.join(process.env.DOTNET_ROOT || path.join(process.env.USERPROFILE, '.dotnet'), 'dotnet.exe');
const TAR = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe');

if (!fs.existsSync(PT)) { console.error('Falta ' + PT + ' (dotnet build app/Puente/Puente.csproj -c Release -r win-x64 -p:PuenteTest=true -o out/puente-test)'); process.exit(2); }

let ok = 0, fail = 0;
const fails = [];
function check(cond, what, extra) {
  if (cond) { ok++; console.log('OK   ' + what); }
  else { fail++; fails.push(what); console.log('FAIL ' + what + (extra ? '\n       ' + String(extra).trim().split('\n').slice(-6).join('\n       ') : '')); }
}
const rnd = () => crypto.randomBytes(4).toString('hex');
const work = path.join(TEMP, 'vigia-ptwork-' + rnd());
fs.mkdirSync(work);
const made = [];   // carpetas vigia-test-* que hay que borrar
function testDir() { const d = path.join(TEMP, 'vigia-test-' + rnd()); fs.mkdirSync(d); made.push(d); return d; }
const sha = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
function sh(exe, args, opt = {}) {
  const r = spawnSync(exe, args, { encoding: 'utf8', timeout: 240000, windowsHide: true, ...opt });
  return { code: r.status, out: (r.stdout || '') + (r.stderr || ''), err: r.error };
}
function reg(args) { return sh('reg.exe', args); }

// ------------------------------------------------------------------ preparación: claves y paquetes de prueba

const keyA = path.join(work, 'claves', 'prueba.pem');
const keyB = path.join(work, 'claves', 'otra.pem');
const genA = sh(PT, ['--genkey', keyA]);
const genB = sh(PT, ['--genkey', keyB]);
const pub = genA.out.trim().split(/\r?\n/).pop();
if (genA.code !== 0 || genB.code !== 0 || !/^[A-Za-z0-9+/=]{60,}$/.test(pub)) { console.error('no se pudo crear la clave de prueba\n' + genA.out + genB.out); process.exit(2); }

const fakeSrc = path.join(work, 'fake-src');
fs.cpSync(path.join(repo, 'tools', 'puente-test', 'fake-vigia'), fakeSrc, { recursive: true });
const fakes = {};
for (const v of ['0.20.17', '0.21.0', '0.21.1', '0.21.2', '0.22.0']) {
  const out = path.join(work, 'fake', v);
  const b = sh(DOTNET, ['build', path.join(fakeSrc, 'FakeVigia.csproj'), '-c', 'Release', '-p:Version=' + v, '-o', out, '-v:q', '-nologo', '-nodeReuse:false']);
  if (b.code !== 0) { console.error('no compiló el Vigía de prueba ' + v + '\n' + b.out); process.exit(2); }
  fs.rmSync(path.join(out, 'Vigia.pdb'), { force: true });
  fakes[v] = out;
}

/** Carpeta «sin conexión» con el zip, su firma y el manifiesto firmado de la versión v. */
function pkg(v, o = {}) {
  const dir = path.join(work, 'pkg', v + (o.tag ? '-' + o.tag : ''));
  const files = path.join(work, 'pkgsrc', v + (o.tag ? '-' + o.tag : ''));
  fs.mkdirSync(dir, { recursive: true });
  fs.cpSync(fakes[v], files, { recursive: true });
  if (o.broken) fs.writeFileSync(path.join(files, 'romper.txt'), 'x');
  const zip = path.join(dir, `Vigia-${v}-win-x64.zip`);
  const names = fs.readdirSync(files);
  const t = sh(TAR, ['-a', '-c', '-f', zip, '-C', files, ...names]);
  if (t.code !== 0) throw new Error('tar: ' + t.out);
  if (o.before) o.before(dir);
  const m = sh(PT, ['--manifest', dir, v, ...(o.min ? [o.min] : [])]);
  if (m.code !== 0) throw new Error('--manifest: ' + m.out);
  const key = o.key || keyA;
  const pu = path.join(dir, 'Puente.exe');
  for (const f of [zip, ...(fs.existsSync(pu) ? [pu] : []), path.join(dir, 'vigia-manifest.json')]) {
    const s = sh(PT, ['--sign', f, key]);
    if (s.code !== 0) throw new Error('--sign: ' + s.out);
  }
  if (o.after) o.after(dir, zip);
  return dir;
}

function env(root, local) {
  return { ...process.env, PUENTE_TEST_ROOT: root, PUENTE_TEST_LOCAL: local, PUENTE_TEST_PUBKEY: pub };
}
function puente(t, args) { return sh(PT, args, { env: env(t.root, t.local) }); }
function install(t, dir, extra = []) { return puente(t, ['--offline', dir, '--quiet', '--yes', ...extra]); }
function state(t) { try { return JSON.parse(fs.readFileSync(path.join(t.root, 'estado.json'), 'utf8')); } catch { return {}; } }
function newPc() { const t = { root: testDir(), local: testDir() }; fs.rmdirSync(t.root); return t; }   // Program Files de prueba aún sin crear
const regBase = t => 'HKCU\\Software\\Vigia-test\\' + path.basename(t.root);
function runValue(t) {
  const r = reg(['query', regBase(t) + '\\Run', '/v', 'Vigia']);
  const m = /Vigia\s+REG_SZ\s+(.+)/.exec(r.out);
  return m ? m[1].trim() : null;
}
const vexe = (t, v) => path.join(t.root, 'versiones', v, 'Vigia.exe');
function readLog(t) { try { const d = path.join(t.root, '_registros'); return fs.readdirSync(d).map(f => fs.readFileSync(path.join(d, f), 'utf8')).join('\n'); } catch { return ''; } }
function asUser(file) { const r = sh(PT, ['--as-user', file]); return { n: r.code, out: r.out }; }

const P = {
  v210: pkg('0.21.0'), v211: pkg('0.21.1'), v220min: pkg('0.22.0', { min: '0.22.0' }),
  broken212: pkg('0.21.2', { broken: true, tag: 'rota' }),
  tamperZip: pkg('0.21.2', { tag: 'zip-cambiado', after: (d, zip) => fs.appendFileSync(zip, 'x') }),
  otherKey: pkg('0.21.2', { tag: 'otra-clave', key: keyB }),
  tamperMan: pkg('0.21.2', {
    tag: 'manifiesto-cambiado', after: d => {
      const f = path.join(d, 'vigia-manifest.json');
      fs.writeFileSync(f, fs.readFileSync(f, 'utf8').replace('"0.21.2"', '"0.21.3"'));
    },
  }),
  legacyOld: pkg('0.20.17'),
  brokenSame: pkg('0.21.1', { broken: true, tag: 'rota' }),
  brokenMin: pkg('0.22.0', { min: '0.22.0', broken: true, tag: 'rota-min' }),
};
// Puente de prueba con un número más alto (2.0.2): para ver que el cambio de Puente es transaccional
const pt202Dir = path.join(work, 'pt202');
{
  // publish: un solo .exe autocontenido, como el Puente.exe que se publica (copiado solo, tiene que arrancar)
  const b = sh(DOTNET, ['publish', path.join(repo, 'app', 'Puente', 'Puente.csproj'), '-c', 'Release', '-r', 'win-x64', '-p:PuenteTest=true', '-p:Version=2.0.2',
    '-o', pt202Dir, '-v:q', '-nologo', '-nodeReuse:false']);
  if (b.code !== 0) { console.error('no compiló el Puente 2.0.2 de prueba\n' + b.out); process.exit(2); }
}
const PT202 = path.join(pt202Dir, 'Puente-test.exe');
/** Paquete que además trae un Puente.exe (el 2.0.2 de prueba), firmado como en una publicación. */
function pkgP(v, o = {}) {
  return pkg(v, { ...o, before: d => { fs.copyFileSync(PT202, path.join(d, 'Puente.exe')); } });
}
const noManifest = path.join(work, 'pkg', 'sin-manifiesto');
fs.mkdirSync(noManifest, { recursive: true });
fs.copyFileSync(path.join(P.v210, 'Vigia-0.21.0-win-x64.zip'), path.join(noManifest, 'Vigia-0.21.0-win-x64.zip'));

try {
  // ---------------------------------------------------------------- argumentos
  console.log('--- argumentos');
  const bogus = sh(PT, ['--bogus'], { env: env(testDir(), testDir()) });
  check(bogus.code === 2, 'un argumento desconocido se rechaza (no acaba en instalar)', bogus.out);
  if (fs.existsSync(PUBLIC_PUENTE)) {
    const pfBefore = fs.existsSync(path.join(process.env.ProgramFiles, 'Vigia'));
    const b2 = sh(PUBLIC_PUENTE, ['--bogus']);
    check(b2.code === 2 && fs.existsSync(path.join(process.env.ProgramFiles, 'Vigia')) === pfBefore,
      'la Puente publicada tampoco instala con un argumento desconocido', b2.out);
    const b3 = sh(PUBLIC_PUENTE, ['--as-user', 'x']);
    check(b3.code === 2, 'la Puente publicada no tiene los comandos de prueba', b3.out);
  }

  // ---------------------------------------------------------------- instalación nueva
  console.log('--- instalación nueva');
  const A = newPc();
  let r = install(A, P.v210);
  let s = state(A);
  check(r.code === 0 && s.current === '0.21.0', 'instala la 0.21.0 en una carpeta nueva', r.out);
  check(fs.existsSync(vexe(A, '0.21.0')) && fs.existsSync(path.join(A.root, 'versiones', '0.21.0', '.vigia-archivos.json')), 'cada versión en versiones\\<ver> con su lista de huellas');
  check(fs.existsSync(path.join(A.root, 'Puente.exe')), 'Puente queda dentro de la carpeta protegida');
  check(!fs.existsSync(path.join(A.root, 'versiones', '0.21.0.preparando')) && !fs.existsSync(path.join(A.root, 'diario.json')), 'no quedan ni la carpeta de preparación ni el diario');
  check(fs.existsSync(path.join(A.root, '_accesos', 'Inicio', 'Vigia.lnk')) && fs.existsSync(path.join(A.root, '_accesos', 'Escritorio', 'Vigia.lnk')), 'accesos directos en Escritorio e Inicio (de prueba)');
  const un = reg(['query', regBase(A) + '\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Vigia', '/v', 'UninstallString']);
  check(/--uninstall/.test(un.out), 'queda registrada en Apps con Puente --uninstall', un.out);
  check(runValue(A) === null, 'sin pedirlo no activa el inicio con Windows');

  console.log('--- permisos (un usuario normal, token sin Administradores)');
  const ctl = path.join(A.local, 'control.txt'); fs.writeFileSync(ctl, 'x');
  let u = asUser(ctl);
  check(u.n === 4 && /administrador=False/.test(u.out), 'control: tu usuario sí escribe en su propia carpeta (la prueba distingue)', u.out);
  for (const f of [vexe(A, '0.21.0'), path.join(A.root, 'versiones', '0.21.0', 'Vigia.dll'), path.join(A.root, 'Puente.exe'), path.join(A.root, 'estado.json')]) {
    u = asUser(f);
    check(u.n === 0 && /administrador=False/.test(u.out), 'un usuario normal no puede cambiar, renombrar, borrar ni poner DLL junto a ' + path.relative(A.root, f), u.out);
  }
  const ic = sh('icacls.exe', [A.root]).out;
  check(!/(Users|Usuarios|Authenticated Users|Usuarios autentificados|Everyone|Todos):\([^)]*\)?\((?:[^)]*[FMW]|.*\b(WD|AD|WA|WEA|DC)\b)/i.test(ic.split('\n').filter(l => /Users|Usuarios|Everyone|Todos/i.test(l)).join('\n').replace(/\(RX\)|\(OI\)|\(CI\)|\(IO\)/g, '')),
    'icacls: Usuarios/Todos solo leen y ejecutan en la raíz', ic);
  const acl = sh(PT, ['--acl-test']);
  check(acl.code === 0, 'Puente --acl-test (detecta permisos de más y enlaces dentro)', acl.out);

  console.log('--- actualizar, al día, versiones viejas');
  r = install(A, P.v210);
  check(r.code === 0 && state(A).current === '0.21.0', 'la misma versión: «al día», no reinstala', r.out);
  r = install(A, P.v211);
  s = state(A);
  check(r.code === 0 && s.current === '0.21.1' && s.previous === '0.21.0' && s.highest === '0.21.1', 'actualiza a la 0.21.1 y guarda la 0.21.0 como anterior', r.out);
  const before = JSON.stringify(state(A));
  r = install(A, P.v210);
  check(r.code !== 0 && JSON.stringify(state(A)) === before, 'un manifiesto viejo (0.21.0, bien firmado) no hace volver atrás', r.out);
  check(fs.readFileSync(path.join(A.root, '_registros', fs.readdirSync(path.join(A.root, '_registros'))[0]), 'utf8').includes('más vieja'), 'el motivo queda en el registro');

  console.log('--- integridad de lo descargado');
  for (const [k, what] of [['tamperZip', 'un zip cambiado después de firmarlo'], ['otherKey', 'un paquete firmado con otra clave'], ['tamperMan', 'un manifiesto editado (firma rota)'], ]) {
    r = install(A, P[k]);
    check(r.code !== 0 && JSON.stringify(state(A)) === before && !fs.existsSync(path.join(A.root, 'versiones', '0.21.2')) && !fs.existsSync(path.join(A.root, 'versiones', '0.21.3')),
      what + ' se rechaza y no se instala nada', r.out);
  }
  {   // relevo de clave: un Puente que confía en la actual y en la nueva acepta lo firmado con la nueva; uno que no, no
    const pubB = genB.out.trim().split(/\r?\n/).pop();
    const R = newPc();
    const relay = { env: { ...env(R.root, R.local), PUENTE_TEST_PUBKEY: pub + ';' + pubB } };
    const rr = sh(PT, ['--offline', P.otherKey, '--quiet', '--yes'], relay);
    check(rr.code === 0 && state(R).current === '0.21.2', 'relevo de clave: con la clave nueva en la lista, lo firmado con ella se instala', rr.out);
    const S = newPc();
    const rs = sh(PT, ['--offline', P.otherKey, '--quiet', '--yes'], { env: env(S.root, S.local) });
    check(rs.code !== 0 && !state(S).current, 'sin la clave nueva en la lista, lo mismo se rechaza', rs.out);
  }
  r = install(A, noManifest);
  check(r.code !== 0 && JSON.stringify(state(A)) === before, 'una versión sin manifiesto firmado (formato viejo) no se instala', r.out);
  const dl = path.join(A.root, '.descargas');
  check(!fs.existsSync(dl) || fs.readdirSync(dl).filter(f => /\.zip$/i.test(f)).length <= 1, 'las descargas viven en la carpeta protegida (.descargas)');
  if (fs.existsSync(dl)) { u = asUser(path.join(A.root, 'estado.json')); check(u.n === 0, 'un usuario normal no puede dejar nada en la raíz ni en .descargas (TOCTOU)', u.out); }

  console.log('--- Puente nuevo + zip que no vale (cambio transaccional de Puente)');
  {
    const ver = f => { const r2 = sh('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', `(Get-Item -LiteralPath '${f}').VersionInfo.FileVersion`]); return r2.out.trim(); };
    const T = newPc();
    install(T, P.v210); install(T, P.v211);
    const pe = path.join(T.root, 'Puente.exe');
    const pHash = sha(pe), pVer = ver(pe);
    const st0 = JSON.stringify(state(T));
    const userData = path.join(T.local, 'mis-datos.txt'); fs.writeFileSync(userData, 'datos del usuario');
    // 1) el zip se cambió después de firmar: Puente no se toca
    r = install(T, pkgP('0.21.2', { tag: 'p202-zip-cambiado', after: (d, zip) => fs.appendFileSync(zip, 'x') }));
    check(r.code !== 0 && sha(pe) === pHash && !fs.existsSync(path.join(T.root, 'Puente.old.exe')) && !fs.existsSync(path.join(T.root, 'diario.json')) && JSON.stringify(state(T)) === st0 && !fs.existsSync(path.join(T.root, 'versiones', '0.21.2')),
      'zip roto + Puente nuevo: no cambia Puente ni Vigía (antes cambiaba Puente y dejaba la versión vieja)', r.out + readLog(T));
    // 2) el zip va bien firmado pero el Puente nuevo no confía en la clave que firmó: Puente no se toca
    const pubB2 = genB.out.trim().split(/\r?\n/).pop();
    const okPkg = pkgP('0.21.2', { tag: 'p202-bien' });
    r = sh(PT, ['--offline', okPkg, '--quiet', '--yes'], { env: { ...env(T.root, T.local), PUENTE_TEST_CHILD_PUBKEY: pubB2 } });
    check(r.code !== 0 && sha(pe) === pHash && JSON.stringify(state(T)) === st0 && /no acepta esta publicación \(código 1\)/.test(r.out + readLog(T)),
      'un Puente nuevo que no aceptaría esta misma publicación no entra (la confianza no se pierde)', r.out + readLog(T));
    // 3) todo bien: Puente cambia de verdad (versión y SHA-256) y Vigía sube
    r = install(T, okPkg);
    check(r.code === 0 && state(T).current === '0.21.2' && sha(pe) === sha(PT202) && sha(pe) !== pHash && /2\.0\.2/.test(ver(pe)) && !/2\.0\.2/.test(pVer),
      'publicación buena: Puente pasa de ' + pVer + ' a 2.0.2 (versión y SHA-256) y Vigía a la 0.21.2', r.out + readLog(T));
    check(/paquete 0\.21\.2 comprobado antes de cambiar Puente/.test(readLog(T)) && /ya estaba bajado y coincide/.test(readLog(T)), 'el paquete se comprueba antes de cambiar Puente y no se baja dos veces', readLog(T));
    // 4) el Puente nuevo sigue confiando: rechaza otra clave, vuelve atrás y no deja bajar de versión
    const st1 = JSON.stringify(state(T));
    r = sh(PT202, ['--offline', P.otherKey, '--quiet', '--yes', '--repair'], { env: env(T.root, T.local) });
    check(r.code !== 0 && JSON.stringify(state(T)) === st1, 'el Puente 2.0.2 rechaza un paquete firmado con una clave que no conoce', r.out);
    r = sh(PT202, ['--offline', P.v210, '--quiet', '--yes'], { env: env(T.root, T.local) });
    check(r.code !== 0 && JSON.stringify(state(T)) === st1, 'el Puente 2.0.2 no deja volver a una versión más vieja con un manifiesto viejo', r.out);
    r = sh(PT202, ['--rollback', '--quiet', '--yes'], { env: env(T.root, T.local) });
    check(r.code === 0 && state(T).current === '0.21.1' && sha(pe) === sha(PT202), 'vuelta atrás con el Puente 2.0.2: Vigía a la 0.21.1 y Puente se queda en 2.0.2', r.out);
    check(fs.readFileSync(userData, 'utf8') === 'datos del usuario', 'los datos del usuario siguen intactos');
    // 5) corte de luz entre apartar el Puente y poner el nuevo: vuelve el que había
    const good2 = sha(pe);
    fs.renameSync(pe, path.join(T.root, 'Puente.old.exe'));
    r = sh(PT202, ['--offline', okPkg, '--quiet', '--yes'], { env: env(T.root, T.local) });
    check(sha(pe) === good2 && !fs.existsSync(path.join(T.root, 'Puente.old.exe')) && /se cortó al cambiarlo/.test(readLog(T)), 'si se cortó al cambiar Puente, el siguiente arranque devuelve el que estaba', r.out + readLog(T));
    // 6) Puente nuevo + versión que no arranca: Puente sí avanza (ya probado), Vigía se queda y la vuelta atrás funciona
    const U = newPc();
    install(U, P.v210); install(U, P.v211);
    r = install(U, pkgP('0.21.2', { broken: true, tag: 'p202-rota' }));
    const pu = path.join(U.root, 'Puente.exe');
    check(r.code !== 0 && state(U).current === '0.21.1' && !fs.existsSync(path.join(U.root, 'versiones', '0.21.2')) && sha(pu) === sha(PT202),
      'Puente nuevo aprobado + Vigía que no arranca: queda la 0.21.1 entera con el Puente nuevo (no una mezcla)', r.out);
    r = sh(PT202, ['--rollback', '--quiet', '--yes'], { env: env(U.root, U.local) });
    check(r.code === 0 && state(U).current === '0.21.0', 'y desde ahí la vuelta atrás funciona', r.out);
  }

  console.log('--- versión que no arranca');
  r = install(A, P.broken212);
  check(r.code !== 0 && state(A).current === '0.21.1' && !fs.existsSync(path.join(A.root, 'versiones', '0.21.2')), 'si la versión nueva falla --install-check, se queda la que había y se borra la nueva', r.out);

  console.log('--- archivos cambiados en la instalación');
  const dll = path.join(A.root, 'versiones', '0.21.1', 'Vigia.dll');
  const good = sha(dll);
  fs.appendFileSync(dll, 'x');
  r = install(A, P.v211);
  check(r.code === 0 && sha(dll) === good && state(A).current === '0.21.1', 'una DLL cambiada se detecta y la versión se reinstala desde el paquete firmado', r.out);
  const planted = path.join(A.root, 'versiones', '0.21.1', 'version.dll');
  fs.writeFileSync(planted, 'MZ');
  r = install(A, P.v211);
  check(r.code === 0 && !fs.existsSync(planted), 'una DLL plantada de más se detecta y se quita al reinstalar', r.out);
  r = install(A, P.v211, ['--repair']);
  check(r.code === 0 && state(A).current === '0.21.1', '--repair reinstala la misma versión', r.out);

  console.log('--- reparar con un paquete que no arranca');
  const v211dir = path.join(A.root, 'versiones', '0.21.1');
  r = install(A, P.brokenSame, ['--repair']);
  check(r.code !== 0 && state(A).current === '0.21.1' && sha(dll) === good && !fs.existsSync(path.join(v211dir, 'romper.txt'))
        && !fs.existsSync(v211dir + '.apartada') && !fs.existsSync(path.join(A.root, 'diario.json')),
    'si la reparación no pasa la prueba, vuelve la carpeta que había (no se queda la que falló)', r.out);
  // corte de luz a mitad de una reparación: la buena apartada, la nueva sin probar en su sitio
  fs.cpSync(v211dir, v211dir + '.apartada', { recursive: true });
  fs.writeFileSync(path.join(v211dir, 'romper.txt'), 'x');
  fs.writeFileSync(path.join(A.root, 'diario.json'), JSON.stringify({ op: 'install', target: '0.21.1', from: '0.21.1', phase: 'verificando' }));
  r = install(A, P.v211);
  check(r.code === 0 && state(A).current === '0.21.1' && !fs.existsSync(path.join(v211dir, 'romper.txt')) && !fs.existsSync(v211dir + '.apartada') && sha(dll) === good,
    'tras un corte a mitad de reparar, Puente devuelve la carpeta apartada', r.out);

  console.log('--- un Puente a la vez');
  const holdLog = path.join(work, 'candado.txt');
  const hfd = fs.openSync(holdLog, 'w');
  const holder = spawn(PT, ['--lock-hold', '20'], { env: env(A.root, A.local), stdio: ['ignore', hfd, hfd], windowsHide: true });
  for (let i = 0; i < 100 && !fs.readFileSync(holdLog, 'utf8').includes('tengo el candado'); i++) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
  const held = fs.readFileSync(holdLog, 'utf8').includes('tengo el candado');
  const before2 = JSON.stringify(state(A));
  r = install(A, P.v211, ['--repair']);
  check(held && r.code !== 0 && /otro Puente/.test(r.out + readLog(A)) && JSON.stringify(state(A)) === before2,
    'mientras otro Puente trabaja, un segundo no instala ni repara (candado)', r.out);
  r = puente(A, ['--rollback', '--quiet', '--yes']);
  check(r.code !== 0 && JSON.stringify(state(A)) === before2, 'ni vuelve atrás', r.out);
  holder.kill();
  for (let i = 0; i < 50 && holder.exitCode === null && holder.signalCode === null; i++) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
  fs.closeSync(hfd);
  for (let i = 0; i < 30 && fs.existsSync(path.join(A.root, '.ocupado')); i++) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
  const ic2 = sh('icacls.exe', [path.join(A.root, '.ocupado')]).out;
  r = install(A, P.v211);
  check(r.code === 0 && state(A).current === '0.21.1', 'al soltarlo, el siguiente Puente sí entra', r.out + ic2);

  console.log('--- volver atrás');
  r = puente(A, ['--rollback', '--quiet', '--yes']);
  s = state(A);
  check(r.code === 0 && s.current === '0.21.0' && s.previous === '0.21.1', 'vuelve a la anterior (0.21.0) y guarda la 0.21.1', r.out);
  r = install(A, P.v211);
  check(r.code === 0 && state(A).current === '0.21.1', 'después puede volver a la 0.21.1 (no es más vieja que la más nueva vista)', r.out);
  r = puente(A, ['--rollback', '--quiet']);
  check(r.code === 0 && state(A).current === '0.21.1', 'sin --yes y sin ventana no vuelve atrás', r.out);
  const prevDll = path.join(A.root, 'versiones', '0.21.0', 'Vigia.dll');
  fs.appendFileSync(prevDll, 'x');
  r = puente(A, ['--rollback', '--quiet', '--yes']);
  check(r.code === 3 && state(A).current === '0.21.1', 'no vuelve a una versión anterior dañada', r.out);

  console.log('--- versión mínima segura');
  {
    const B = newPc();
    install(B, P.v211);
    r = install(B, P.brokenMin);
    check(r.code !== 0 && state(B).current === '0.21.1' && state(B).floor === '0.22.0',
      'la mínima segura de un manifiesto firmado se guarda aunque esa instalación falle', r.out);
    r = install(B, P.v211, ['--repair']);
    check(r.code !== 0 && state(B).current === '0.21.1', 'y después ya no se reinstala una versión por debajo de ella', r.out);
  }
  r = install(A, P.v220min);
  s = state(A);
  check(r.code === 0 && s.current === '0.22.0' && s.floor === '0.22.0', 'la 0.22.0 sube la mínima segura a 0.22.0', r.out);
  r = puente(A, ['--rollback', '--quiet', '--yes']);
  check(r.code === 2 && state(A).current === '0.22.0', 'no deja volver por debajo de la mínima segura', r.out);
  // el piso lo guarda estado.json, que un usuario normal no puede editar (comprobado arriba)

  console.log('--- cortes a mitad');
  fs.mkdirSync(path.join(A.root, 'versiones', '0.22.1.preparando'), { recursive: true });
  fs.writeFileSync(path.join(A.root, 'versiones', '0.22.1.preparando', 'Vigia.exe'), 'a medias');
  fs.writeFileSync(path.join(A.root, 'diario.json'), JSON.stringify({ op: 'install', target: '0.22.1', from: '0.22.0', phase: 'preparando' }));
  r = install(A, P.v220min);
  check(r.code === 0 && state(A).current === '0.22.0' && !fs.existsSync(path.join(A.root, 'versiones', '0.22.1.preparando')) && !fs.existsSync(path.join(A.root, 'diario.json')),
    'un corte mientras se preparaba: se limpia lo a medias y sigue la versión que había', r.out);
  // corte justo al activar, con la versión nueva rota: vuelve a la anterior solo si no está por debajo del piso
  const B = newPc();
  install(B, P.v210); install(B, P.v211);
  fs.appendFileSync(path.join(B.root, 'versiones', '0.21.1', 'Vigia.dll'), 'x');
  fs.writeFileSync(path.join(B.root, 'diario.json'), JSON.stringify({ op: 'install', target: '0.21.1', from: '0.21.0', phase: 'activando' }));
  r = puente(B, ['--rollback', '--quiet']);   // cualquier comando con permisos revisa el diario primero
  s = state(B);
  check(s.current === '0.21.0' && !fs.existsSync(path.join(B.root, 'diario.json')), 'un corte al activar con la versión nueva dañada: vuelve a la que funcionaba', r.out + JSON.stringify(s));

  console.log('--- carpeta de instalación sospechosa');
  const C = newPc();
  fs.mkdirSync(C.root); fs.writeFileSync(path.join(C.root, 'algo.txt'), 'de otro');
  r = install(C, P.v210);
  check(r.code !== 0 && fs.existsSync(path.join(C.root, 'algo.txt')) && !fs.existsSync(path.join(C.root, 'versiones')), 'no se instala encima de una carpeta que no es de Puente (y no se toca)', r.out);
  const D = newPc();
  const elsewhere = testDir();
  fs.writeFileSync(path.join(elsewhere, 'canario.txt'), 'no tocar');
  fs.symlinkSync(elsewhere, D.root, 'junction');
  r = install(D, P.v210);
  check(r.code !== 0 && fs.existsSync(path.join(elsewhere, 'canario.txt')) && !fs.existsSync(path.join(elsewhere, 'versiones')), 'no se instala si la carpeta es un junction', r.out);
  fs.rmdirSync(D.root);

  // ---------------------------------------------------------------- migración desde %LOCALAPPDATA%\Programs
  console.log('--- migración desde la instalación vieja');
  function legacyPc(o = {}) {
    const t = newPc();
    const progs = path.join(t.local, 'Programs');
    fs.cpSync(fakes['0.20.17'], path.join(progs, 'Vigia'), { recursive: true });
    fs.mkdirSync(path.join(progs, 'Vigia-Puente'), { recursive: true });
    fs.writeFileSync(path.join(progs, 'Vigia-Puente', 'Puente.exe'), 'puente viejo');
    fs.mkdirSync(path.join(progs, 'Vigia.anterior'), { recursive: true });
    fs.writeFileSync(path.join(progs, 'Vigia.anterior', 'Vigia.exe'), 'vieja');
    const data = path.join(t.local, 'Vigia');
    fs.mkdirSync(path.join(data, 'copias'), { recursive: true });
    fs.writeFileSync(path.join(data, 'settings.json'), '{"lang":"es","tema":"oscuro"}');
    fs.writeFileSync(path.join(data, 'copias', '2026-10-01.json'), '{"items":[]}');
    fs.writeFileSync(path.join(data, 'apps-history.json'), '[]');
    fs.mkdirSync(path.join(t.local, '_accesos-viejos'), { recursive: true });
    fs.writeFileSync(path.join(t.local, '_accesos-viejos', 'Vigia.lnk'), 'x');
    const legacyExe = path.join(progs, 'Vigia', 'Vigia.exe');
    const taskExe = o.foreignTask ? 'C:\\Tools\\otro.exe' : legacyExe;
    fs.writeFileSync(path.join(t.local, '_tarea-vieja.xml'),
      `<?xml version="1.0"?><Task><Settings><Enabled>true</Enabled></Settings><Principals><Principal><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Actions><Exec><Command>"${taskExe}"</Command><Arguments>--tray</Arguments></Exec></Actions></Task>`);
    if (o.run) reg(['add', regBase(t) + '\\Run', '/v', 'Vigia', '/t', 'REG_SZ', '/d', `"${legacyExe}" --tray`, '/f']);
    reg(['add', regBase(t) + '\\Run', '/v', 'OtroPrograma', '/t', 'REG_SZ', '/d', '"C:\\Tools\\otro.exe"', '/f']);
    return { t, data, progs };
  }
  const dataHashes = d => Object.fromEntries(['settings.json', 'copias\\2026-10-01.json', 'apps-history.json'].map(f => [f, sha(path.join(d, f))]));

  const L = legacyPc();
  const h0 = dataHashes(L.data);
  r = install(L.t, P.v210);
  s = state(L.t);
  check(r.code === 0 && s.current === '0.21.0' && s.legacy === '0.20.17', 'detecta la 0.20.17 vieja y migra a Archivos de programa', r.out);
  check(JSON.stringify(dataHashes(L.data)) === JSON.stringify(h0), 'ajustes, copias e historial del usuario quedan intactos');
  check(!fs.existsSync(path.join(L.progs, 'Vigia')) && !fs.existsSync(path.join(L.progs, 'Vigia-Puente')) && !fs.existsSync(path.join(L.progs, 'Vigia.anterior')),
    'la instalación vieja (y su Puente) se retira solo después de verificar la nueva');
  check(!fs.existsSync(path.join(L.t.local, '_tarea-vieja.xml')), 'la tarea de inicio vieja (elevada) de Vigía se quita');
  check(runValue(L.t) === `"${vexe(L.t, '0.21.0')}" --tray`, 'el inicio con Windows sigue activo, ahora en Run de tu usuario y sin permisos', runValue(L.t));
  check(/otro\.exe/.test(reg(['query', regBase(L.t) + '\\Run', '/v', 'OtroPrograma']).out), 'la entrada de inicio de otro programa no se toca');
  check(!fs.existsSync(path.join(L.t.local, '_accesos-viejos', 'Vigia.lnk')), 'los accesos viejos se quitan');
  r = install(L.t, P.v211);
  check(r.code === 0 && runValue(L.t) === `"${vexe(L.t, '0.21.1')}" --tray`, 'al actualizar, el inicio con Windows apunta a la versión nueva', r.out);

  const L2 = legacyPc({ run: true, foreignTask: true });
  r = install(L2.t, P.v210);
  check(r.code === 0 && fs.existsSync(path.join(L2.t.local, '_tarea-vieja.xml')), 'una tarea con ese nombre pero de otro programa no se borra', r.out);
  check(runValue(L2.t) === `"${vexe(L2.t, '0.21.0')}" --tray`, 'una entrada Run vieja de Vigía se pasa a la versión nueva');

  const L3 = legacyPc();
  const before3 = dataHashes(L3.data);
  r = install(L3.t, P.broken212);
  check(r.code !== 0 && fs.existsSync(path.join(L3.progs, 'Vigia', 'Vigia.exe')) && fs.existsSync(path.join(L3.t.local, '_tarea-vieja.xml')) && JSON.stringify(dataHashes(L3.data)) === JSON.stringify(before3),
    'si la versión nueva falla, la instalación vieja sigue entera y funcionando (no se retira nada)', r.out);

  const L4 = legacyPc();
  const target = testDir();
  fs.writeFileSync(path.join(target, 'canario.txt'), 'no tocar');
  fs.rmSync(path.join(L4.progs, 'Vigia'), { recursive: true });
  fs.cpSync(fakes['0.20.17'], target, { recursive: true });
  fs.symlinkSync(target, path.join(L4.progs, 'Vigia'), 'junction');
  r = install(L4.t, P.v210);
  check(fs.existsSync(path.join(target, 'canario.txt')), 'si la carpeta vieja es un junction, no se borra lo que hay detrás', r.out);
  try { fs.rmdirSync(path.join(L4.progs, 'Vigia')); } catch { }

  // ---------------------------------------------------------------- clave de firma
  console.log('--- clave de firma');
  const kenv = { env: { ...process.env, PUENTE_TEST_PUBKEY: pub } };
  r = sh(PT, ['--key-check', keyA]);
  check(r.code === 0, '--genkey crea la clave con permisos propios: solo tu cuenta (no hereda los de la carpeta)', r.out);
  const keyOpen = path.join(work, 'claves', 'abierta.pem');
  fs.copyFileSync(keyA, keyOpen);
  sh(path.join(process.env.SystemRoot, 'System32', 'icacls.exe'), [keyOpen, '/grant', '*S-1-5-32-545:R']);   // Usuarios: leer
  r = sh(PT, ['--key-check', keyOpen]);
  check(r.code === 3 && /otras cuentas/.test(r.out), 'una clave que otro grupo puede leer sale como NO protegida', r.out);
  const sigProbe = path.join(work, 'probar-firma.txt'); fs.writeFileSync(sigProbe, 'x');
  r = sh(PT, ['--sign', sigProbe, keyOpen]);
  check(r.code === 3 && !fs.existsSync(sigProbe + '.sig'), 'no se firma con una clave que otro grupo puede leer', r.out);
  r = sh(PT, ['--sign', sigProbe, keyA]);
  check(r.code === 0 && fs.existsSync(sigProbe + '.sig'), 'con la clave protegida sí se firma', r.out);

  function relDir(tag) {
    const d = path.join(work, 'rel-' + tag); fs.mkdirSync(d);
    fs.copyFileSync(path.join(P.v210, 'Vigia-0.21.0-win-x64.zip'), path.join(d, 'Vigia-0.21.0-win-x64.zip'));
    fs.copyFileSync(PT, path.join(d, 'Puente.exe')); fs.copyFileSync(PT, path.join(d, 'Vigia-Setup.exe'));
    const m = sh(PT, ['--manifest', d, '0.21.0'], kenv);
    if (m.code !== 0) throw new Error('--manifest: ' + m.out);
    return d;
  }
  const sigs = d => fs.readdirSync(d).filter(f => f.endsWith('.sig'));
  let rd = relDir('bien');
  r = sh(PT, ['--sign-release', rd, keyA], kenv);
  check(r.code === 0 && sigs(rd).length === 4 && sh(PT, ['--verify-manifest', path.join(rd, 'vigia-manifest.json')], kenv).code === 0,
    '--sign-release firma las cuatro piezas y comprueba el manifiesto', r.out);
  r = sh(PT, ['--sign-release', rd, keyA], kenv);
  check(r.code === 1 && /no se pisan/.test(r.out), '--sign-release no pisa firmas que ya existen', r.out);
  rd = relDir('otra');
  r = sh(PT, ['--sign-release', rd, keyB], kenv);
  check(r.code === 1 && sigs(rd).length === 0, 'con una clave que no es la de Vigía no deja firmas a medias', r.out);
  rd = relDir('abierta');
  r = sh(PT, ['--sign-release', rd, keyOpen], kenv);
  check(r.code === 3 && sigs(rd).length === 0, '--sign-release no usa una clave que otro grupo puede leer', r.out);
  rd = relDir('setup');
  fs.appendFileSync(path.join(rd, 'Vigia-Setup.exe'), 'x');
  r = sh(PT, ['--sign-release', rd, keyA], kenv);
  check(r.code === 1 && sigs(rd).length === 0, 'si Vigia-Setup.exe no es el mismo Puente, no se firma', r.out);
  rd = relDir('zip');
  fs.appendFileSync(path.join(rd, 'Vigia-0.21.0-win-x64.zip'), 'x');   // el paquete cambió después del manifiesto
  r = sh(PT, ['--sign-release', rd, keyA], kenv);
  check(r.code === 1 && sigs(rd).length === 0, 'si el paquete cambió después del manifiesto, se quitan las firmas', r.out);

  // ---------------------------------------------------------------- quitar
  console.log('--- desinstalar');
  r = puente(L.t, ['--uninstall', '--quiet']);
  check(r.code === 0 && fs.existsSync(path.join(L.t.root, 'versiones')), 'sin --yes y sin ventana no quita nada', r.out);
  r = puente(L.t, ['--uninstall', '--quiet', '--yes']);
  const left = fs.existsSync(L.t.root) ? fs.readdirSync(L.t.root).filter(f => f !== '_registros') : [];
  check(r.code === 0 && left.length === 0, 'quita la instalación entera', r.out + ' quedan: ' + left.join(', '));
  check(runValue(L.t) === null && !/UninstallString/.test(reg(['query', regBase(L.t) + '\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Vigia']).out),
    'quita el inicio con Windows y la entrada de Apps');
  check(JSON.stringify(dataHashes(L.data)) === JSON.stringify(h0), 'tus datos (%LOCALAPPDATA%\\Vigia) se quedan al desinstalar');
} finally {
  for (const d of made) {
    try { reg(['delete', 'HKCU\\Software\\Vigia-test\\' + path.basename(d), '/f']); } catch { }
    try { fs.rmSync(d, { recursive: true, force: true }); } catch { }
  }
  try { const r = reg(['query', 'HKCU\\Software\\Vigia-test']); if (r.code === 0 && !/\\Vigia-test\\/.test(r.out)) reg(['delete', 'HKCU\\Software\\Vigia-test', '/f']); } catch { }
  try { fs.rmSync(work, { recursive: true, force: true }); } catch { }
}
console.log(`\npuente-test: ${ok} OK, ${fail} FAIL`);
if (fail) console.log('fallaron:\n  ' + fails.join('\n  '));
process.exit(fail ? 1 : 0);
