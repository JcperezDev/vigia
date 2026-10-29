// Pruebas de tools/release.sh contra un GitHub de mentira (tools/release-mock.js) en 127.0.0.1.
// No usa la red, ni la clave de publicar, ni git push: firma con una clave de prueba hecha en %TEMP% y comprueba
// con out/puente-test/Puente-test.exe (la compilación de pruebas, que acepta esa clave).
//   node tools/release-test.js        sale con 1 si algo falla
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { spawn, spawnSync } = require('child_process');
const { createMock } = require('./release-mock');

const ROOT = path.resolve(__dirname, '..');
const PT = path.join(ROOT, 'out', 'puente-test', 'Puente-test.exe');
// git-bash: VIGIA_BASH, la instalación normal de Git, o la que tenga git (nunca el bash de WSL de System32)
const gitCore = spawnSync('git', ['--exec-path'], { encoding: 'utf8' }).stdout?.trim();
const BASH = [process.env.VIGIA_BASH, 'C:/Program Files/Git/bin/bash.exe', 'C:/Program Files/Git/usr/bin/bash.exe',
  ...(gitCore ? [path.join(gitCore, '../../../bin/bash.exe'), path.join(gitCore, '../../../usr/bin/bash.exe')] : [])]
  .find((p) => p && fs.existsSync(p));
if (!fs.existsSync(PT)) { console.error('Falta ' + PT + ': dotnet build app/Puente/Puente.csproj -c Release -r win-x64 -p:PuenteTest=true -o out/puente-test'); process.exit(1); }
if (!BASH) { console.error('Falta git-bash.'); process.exit(1); }

const V = /<Version>([^<]+)<\/Version>/.exec(fs.readFileSync(path.join(ROOT, 'app/Vigia/Vigia.csproj'), 'utf8'))[1];
const TMP = fs.mkdtempSync(path.join(fs.realpathSync.native(os.tmpdir()), 'vigia-reltest-'));
let ok = 0, fail = 0;
const check = (c, name, extra) => { if (c) { ok++; console.log('OK   ' + name); } else { fail++; console.log('FAIL ' + name + (extra ? '\n     ' + String(extra).slice(-1200).replace(/\n/g, '\n     ') : '')); } };
const sh = (exe, args, env) => spawnSync(exe, args, { encoding: 'utf8', env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'] });

// ---------------------------------------------------------------- artefactos de prueba (como los pasos 3-5)
const KEY = path.join(TMP, 'clave-prueba.pem');
const PUB = sh(PT, ['--genkey', KEY]).stdout.trim();
const ENVP = { PUENTE_TEST_PUBKEY: PUB };
function artifacts(dir) {
  fs.mkdirSync(dir, { recursive: true });
  const zip = `Vigia-${V}-win-x64.zip`;
  const src = path.join(dir, 'src'); fs.mkdirSync(src);
  fs.writeFileSync(path.join(src, 'leeme.txt'), 'paquete de prueba ' + crypto.randomBytes(8).toString('hex'));
  spawnSync('C:/Windows/System32/tar.exe', ['-a', '-c', '-f', path.join(dir, zip), 'leeme.txt'], { cwd: src });
  fs.rmSync(src, { recursive: true });
  fs.copyFileSync(PT, path.join(dir, 'Puente.exe'));
  fs.copyFileSync(PT, path.join(dir, 'Vigia-Setup.exe'));
  const m = sh(PT, ['--manifest', dir, V, ''], ENVP);
  if (m.status !== 0) throw new Error('manifest: ' + m.stderr);
  for (const f of [zip, 'Puente.exe', 'Vigia-Setup.exe', 'vigia-manifest.json']) {
    const s = sh(PT, ['--sign', path.join(dir, f), KEY], ENVP);
    if (s.status !== 0) throw new Error('sign ' + f + ': ' + s.stderr);
  }
  const names = [zip, zip + '.sig', 'Puente.exe', 'Puente.exe.sig', 'Vigia-Setup.exe', 'Vigia-Setup.exe.sig', 'vigia-manifest.json', 'vigia-manifest.json.sig'];
  fs.writeFileSync(path.join(dir, 'SHA256SUMS.txt'), names.map((n) =>
    crypto.createHash('sha256').update(fs.readFileSync(path.join(dir, n))).digest('hex') + ' *' + n).join('\n') + '\n');
  return { dir, zip, names: [...names, 'SHA256SUMS.txt'] };
}

function release(mock, out, repo, mirror, extraEnv = {}) {
  return new Promise((done) => {
    const env = { ...process.env, ...ENVP, VIGIA_RELEASE_API: `http://127.0.0.1:${mock.port}`, VIGIA_RELEASE_SKIP_BUILD: '1',
      VIGIA_RELEASE_OUT: out.dir, VIGIA_RELEASE_PUENTE: PT, VIGIA_RELEASE_TEST_KEY: KEY,
      VIGIA_RELEASE_REPO: repo, VIGIA_RELEASE_MIRROR: mirror, ...extraEnv };
    const c = spawn(BASH, ['tools/release.sh', 'notas de prueba'], { cwd: ROOT, env, stdio: ['ignore', 'pipe', 'pipe'] });
    let text = '';
    c.stdout.on('data', (d) => (text += d)); c.stderr.on('data', (d) => (text += d));
    c.on('close', (code) => done({ code, out: text }));
  });
}
const rels = (mock, repo) => (mock.repos[repo]?.releases ?? []).filter((r) => r.tag_name === 'v' + V);
const writes = (mock, from) => mock.log.slice(from).filter((l) => l.method !== 'GET');

(async () => {
  const mock = createMock();
  await mock.start();
  try {
    // versiones ya publicadas (como las de verdad): nunca se tocan
    const seeds = ['prueba/vigia', 'prueba/espejo', 'prueba/b', 'prueba/c', 'prueba/d'].map((r) => mock.seed(r, 'v0.20.17').id);

    // ---- 1. se corta una subida: queda un borrador, nada publicado
    const A = artifacts(path.join(TMP, 'a'));
    mock.failUpload = 'Puente.exe';
    let r = await release(mock, A, 'prueba/vigia', 'prueba/espejo');
    check(r.code !== 0, 'una subida que falla para el script', r.out);
    check(rels(mock, 'prueba/vigia').length === 1 && rels(mock, 'prueba/vigia')[0].draft === true, 'queda como borrador (nadie la ve ni la instala)', r.out);
    check(rels(mock, 'prueba/espejo').length === 0, 'el espejo ni se empezó');
    check(!mock.log.some((l) => l.method === 'PATCH'), 'no se publicó nada');
    mock.failUpload = null;

    // ---- 2. volver a ejecutar: sigue con el borrador y publica
    let from = mock.log.length;
    r = await release(mock, A, 'prueba/vigia', 'prueba/espejo');
    check(r.code === 0, 'al reintentar termina bien', r.out);
    check(rels(mock, 'prueba/vigia').length === 1, 'reusa el borrador en vez de crear otro');
    check(/ya estaba/.test(r.out) && writes(mock, from).filter((l) => l.path.startsWith('/repos/prueba/vigia/') && l.path.endsWith('name=' + A.zip)).length === 0, 'no vuelve a subir lo que ya estaba bien', r.out);
    for (const repo of ['prueba/vigia', 'prueba/espejo']) {
      const rel = rels(mock, repo)[0];
      check(rel && rel.draft === false, `publicada en ${repo}`);
      const names = (rel?.assets ?? []).map((a) => a.name).sort();
      check(JSON.stringify(names) === JSON.stringify([...A.names].sort()), `${repo}: los ${A.names.length} archivos, ni uno más ni uno menos`, names.join(', '));
      const same = (rel?.assets ?? []).every((a) => Buffer.compare(a.data, fs.readFileSync(path.join(A.dir, a.name))) === 0);
      check(same, `${repo}: cada archivo subido es idéntico al firmado`);
    }
    const order = mock.log.map((l, i) => ({ ...l, i }));
    const lastUpload = Math.max(...order.filter((l) => l.method === 'POST' && l.path.includes('/assets?name=')).map((l) => l.i));
    const firstPublish = Math.min(...order.filter((l) => l.method === 'PATCH').map((l) => l.i));
    check(firstPublish > lastUpload, 'solo publica después de subir y comprobar todo');

    // ---- 3. otra vez: ya publicada, no se toca
    from = mock.log.length;
    r = await release(mock, A, 'prueba/vigia', 'prueba/espejo');
    check(r.code !== 0 && /ya está publicada/.test(r.out), 'una versión ya publicada no se vuelve a publicar', r.out);
    check(writes(mock, from).length === 0, 'y no se cambia nada en GitHub');

    // ---- 4. lo subido no es lo firmado: no se publica
    const B = artifacts(path.join(TMP, 'b'));
    mock.corrupt = 'vigia-manifest.json';
    r = await release(mock, B, 'prueba/b', '');
    mock.corrupt = null;
    check(r.code !== 0 && rels(mock, 'prueba/b')[0]?.draft === true, 'si el manifiesto que vuelve de GitHub no es el mismo, se queda en borrador', r.out);

    // ---- 4b. el zip que vuelve de GitHub no es el firmado (no solo el manifiesto): no se publica
    mock.seed('prueba/f', 'v0.20.17');
    const F = artifacts(path.join(TMP, 'f'));
    mock.corrupt = F.zip;
    r = await release(mock, F, 'prueba/f', '');
    mock.corrupt = null;
    check(r.code !== 0 && rels(mock, 'prueba/f')[0]?.draft === true, 'si el paquete que vuelve de GitHub no es el firmado, se queda en borrador', r.out);

    // ---- 4c. un borrador viejo con un archivo del mismo tamaño pero otros bytes: se vuelve a subir
    mock.seed('prueba/e', 'v0.20.17');
    const E = artifacts(path.join(TMP, 'e'));
    mock.failUpload = 'Puente.exe';
    await release(mock, E, 'prueba/e', '');
    mock.failUpload = null;
    const stale = rels(mock, 'prueba/e')[0]?.assets.find((a) => a.name === E.zip);
    if (stale) { stale.data = Buffer.from(stale.data); stale.data[stale.data.length - 1] ^= 1; }
    from = mock.log.length;
    r = await release(mock, E, 'prueba/e', '');
    const relE = rels(mock, 'prueba/e')[0];
    check(stale && r.code === 0 && relE?.draft === false && writes(mock, from).some((l) => l.path.endsWith('name=' + E.zip))
          && Buffer.compare(relE.assets.find((a) => a.name === E.zip).data, fs.readFileSync(path.join(E.dir, E.zip))) === 0,
      'mismo tamaño no basta: un archivo viejo con otros bytes se vuelve a subir antes de publicar', r.out);

    // ---- 5. el borrador tiene un archivo de más: no se publica
    const C = artifacts(path.join(TMP, 'c'));
    mock.extra = true;
    r = await release(mock, C, 'prueba/c', '');
    check(r.code !== 0 && /sobran/.test(r.out) && rels(mock, 'prueba/c')[0]?.draft === true, 'un archivo de más en el borrador: no se publica', r.out);

    // ---- 6. un archivo cambiado después de firmar: para antes de tocar la red
    const D = artifacts(path.join(TMP, 'd'));
    fs.appendFileSync(path.join(D.dir, D.zip), 'x');
    from = mock.log.length;
    r = await release(mock, D, 'prueba/d', '');
    check(r.code !== 0 && mock.log.length === from, 'un paquete cambiado tras firmarlo para en el paso 6, sin tocar la red', r.out);

    // ---- 7. solo GitHub o 127.0.0.1
    r = await release(mock, D, 'prueba/d', '', { VIGIA_RELEASE_API: 'https://ejemplo.invalid' });
    check(r.code !== 0 && /solo puede ser GitHub/.test(r.out), 'no acepta otro servidor', r.out);

    // ---- 8. la clave dentro del repo se rechaza (copia de prueba, se borra enseguida)
    const inRepo = path.join(ROOT, 'out', 'clave-prueba-' + crypto.randomBytes(4).toString('hex') + '.pem');
    fs.copyFileSync(KEY, inRepo);
    try {
      r = await release(mock, D, 'prueba/d', '', { VIGIA_RELEASE_TEST_KEY: inRepo });
      check(r.code !== 0 && /no puede estar dentro del repositorio/.test(r.out), 'una clave dentro del repositorio se rechaza', r.out);
    } finally { fs.rmSync(inRepo, { force: true }); }

    // ---- 9. contra GitHub de verdad no se puede cambiar el repo. Sin red: DRY y un perfil vacío
    //         (si el control fallara, el script pararía por falta de clave, nunca con la real)
    const noHome = path.join(TMP, 'perfil-vacio');
    fs.mkdirSync(noHome, { recursive: true });
    from = mock.log.length;
    r = await release(mock, D, 'prueba/otro', 'prueba/espejo',
      { VIGIA_RELEASE_API: 'https://api.github.com', VIGIA_RELEASE_DRY: '1', USERPROFILE: noHome, HOME: noHome });
    check(r.code !== 0 && /solo se pueden cambiar con el servidor de pruebas/.test(r.out), 'contra GitHub real el repo no se cambia con una variable', r.out);
    check(mock.log.length === from, 'y para antes de tocar nada');

    check(mock.touchedPublished(seeds).length === 0, 'las versiones ya publicadas (v0.20.17) no se tocaron en ninguna prueba',
      JSON.stringify(mock.touchedPublished(seeds)));
    check(!mock.log.some((l) => /make_latest/.test(l.path)), 'no cambia la marca de «última» de nada existente');
  } finally {
    await mock.stop();
    fs.rmSync(TMP, { recursive: true, force: true });
  }
  console.log(`\nrelease-test: ${ok} OK, ${fail} FAIL`);
  process.exit(fail ? 1 : 0);
})().catch((e) => { console.error(e); fs.rmSync(TMP, { recursive: true, force: true }); process.exit(1); });
