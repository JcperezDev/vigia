#!/usr/bin/env node
// Prueba de extremo a extremo del proveedor bitwarden-ssh-agent de VigiaFirma con un agente SSH de LABORATORIO
// (tools/signing/agente-lab.js; su salida se lee tras una pausa porque spawnSync bloquea el bucle de eventos)
// Clave Ed25519 de usar y tirar. No habla con Bitwarden ni toca claves reales.
//
// Uso: node tools/signing/agente-test.js <VigiaFirma.exe> <Puente-test.exe>
'use strict';
const fs = require('fs'), os = require('os'), path = require('path'), crypto = require('crypto');
const { spawn, spawnSync } = require('child_process');

const [vf, pt] = process.argv.slice(2);
if (!vf || !pt || !fs.existsSync(vf) || !fs.existsSync(pt)) { console.error('uso: node tools/signing/agente-test.js <VigiaFirma.exe> <Puente-test.exe>'); process.exit(2); }
const here = __dirname;
let ok = 0, fail = 0;
const check = (c, n) => { console.log((c ? 'OK   ' : 'FAIL ') + n); c ? ok++ : fail++; };
const sha = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'agente-test-'));
const sleep = ms => new Promise(r => setTimeout(r, ms));

function makeVersion(dir, ver) {
  fs.mkdirSync(dir, { recursive: true });
  const zip = path.join(dir, `Vigia-${ver}-win-x64.zip`), pu = path.join(dir, 'Puente.exe');
  fs.writeFileSync(zip, crypto.randomBytes(200000)); fs.writeFileSync(pu, crypto.randomBytes(90000));
  fs.copyFileSync(pu, path.join(dir, 'Vigia-Setup.exe'));
  const piece = f => ({ name: path.basename(f), size: fs.statSync(f).size, sha256: sha(f) });
  fs.writeFileSync(path.join(dir, 'vigia-manifest.json'), JSON.stringify({ schema: 1, product: 'Vigia', version: ver, minimum: '0.20.0', package: piece(zip), puente: piece(pu) }, null, 2));
  const h = path.join(dir, '..', path.basename(dir) + '-huellas.txt');
  fs.writeFileSync(h, spawnSync(vf, ['huellas', dir], { encoding: 'utf8' }).stdout);
  return h;
}

async function withAgent(mode, fn) {
  const pipe = 'vigia-agente-lab-' + crypto.randomBytes(4).toString('hex');
  const pubSsh = path.join(tmp, pipe + '.ssh.txt');
  const ag = spawn(process.execPath, [path.join(here, 'agente-lab.js'), pipe, mode, pubSsh], { stdio: ['ignore', 'pipe', 'inherit'] });
  let log = '';
  ag.stdout.on('data', d => { log += d; });
  for (let i = 0; i < 100 && !log.includes('listo'); i++) await sleep(50);
  const env = { ...process.env, VIGIA_AGENTE_PIPE: pipe };
  try { return await fn({ env, pubSsh, log: () => log }); } finally { ag.kill(); }
}

const run = (args, env, input) => spawnSync(vf, args, { encoding: 'utf8', env, input, timeout: 60000 });

(async () => {
  try {
    // ---- 1. firma correcta, verificada por VigiaFirma, por OpenSSL (verificar-b.js) y por Puente
    await withAgent('ok', async ({ env, pubSsh, log }) => {
      const keys = path.join(tmp, 'claves');
      let r = run(['publica-ssh', fs.readFileSync(pubSsh, 'utf8').trim(), keys, 'labB'], env);
      check(r.status === 0, 'publica-ssh: clave pública del agente -> vigia-labB.publica.txt');
      const pub = path.join(keys, 'vigia-labB.publica.txt');
      r = run(['publica-ssh', '-----BEGIN OPENSSH PRIVATE KEY-----', keys, 'x'], env);
      check(r.status !== 0 && /PRIVADA/.test(r.stderr), 'publica-ssh rechaza algo que parece una clave privada');
      r = run(['agente', pub], env);
      check(r.status === 0 && /es la de vigia-labB/.test(r.stdout), 'agente: la clave pública B está en el agente');
      await sleep(300); const before = log();
      r = run(['prueba-agente', pub, path.join(tmp, 'prueba.txt')], env, 'PRUEBA\n');
      check(r.status === 0 && /firma Ed25519 de 64 bytes/.test(r.stdout), 'prueba-agente: firma de prueba Ed25519 verificada');
      r = spawnSync(process.execPath, [path.join(here, 'verificar-b.js'), pub, '--prueba', path.join(tmp, 'prueba.txt')], { encoding: 'utf8' });
      check(r.status === 0, 'verificar-b.js (OpenSSL) valida la firma de prueba por su cuenta');
      await sleep(300); check((log().slice(before.length).match(/peticion 13/g) || []).length === 1, 'prueba-agente pidió exactamente una firma');

      const dir = path.join(tmp, 'v1'), hu = makeVersion(dir, '0.21.3');
      await sleep(300); const n0 = (log().match(/peticion 13/g) || []).length;
      r = run(['firmar-agente', dir, pub, hu], env, 'NO\n');
      check(r.status === 1 && !fs.existsSync(path.join(dir, 'vigia-manifest.json.sig')), 'firmar-agente: sin escribir "FIRMAR 0.21.3" no pide firma ni escribe nada');
      await sleep(300); check((log().match(/peticion 13/g) || []).length === n0, 'firmar-agente cancelado no llegó a pedir la firma al agente');
      r = run(['firmar-agente', dir, pub, hu], env, 'FIRMAR 0.21.2\n');
      check(r.status === 1, 'firmar-agente: confirmar otra versión no vale');
      r = run(['firmar-agente', dir, pub, hu], env, 'FIRMAR 0.21.3\n');
      check(r.status === 0, 'firmar-agente: firma la versión tras escribir "FIRMAR 0.21.3"');
      check(/VERSIÓN 0\.21\.3/.test(r.stdout) && r.stdout.includes(sha(path.join(dir, 'vigia-manifest.json'))), 'firmar-agente enseña versión y SHA-256 antes de firmar');
      await sleep(300); check((log().match(/peticion 13/g) || []).length === n0 + 1, 'una sola aprobación en el agente para las cuatro piezas');
      r = run(['verificar', dir, pub, hu], env);
      check(r.status === 0, 'VigiaFirma verificar: firmas de B, manifiesto y huellas correctos');
      const files = fs.readdirSync(dir).filter(f => !f.endsWith('.sig') && f !== 'firma-b.txt').map(f => path.join(dir, f));
      r = spawnSync(process.execPath, [path.join(here, 'verificar-b.js'), pub, ...files], { encoding: 'utf8' });
      check(r.status === 0, 'verificar-b.js (OpenSSL) valida las cuatro firmas por su cuenta');
      const spkiB = fs.readFileSync(pub, 'utf8').match(/^publica (\S+)/m)[1];
      const A = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' }).publicKey.export({ format: 'der', type: 'spki' }).toString('base64');
      const pv = (f, keys) => spawnSync(pt, ['--verify', f], { encoding: 'utf8', env: { ...process.env, PUENTE_TEST_PUBKEY: keys.join(';') } }).status;
      check(files.every(f => pv(f, [A, spkiB]) === 0), 'Puente acepta las cuatro piezas firmadas por el agente con A+B de confianza');
      check(files.every(f => pv(f, [A]) === 1), 'Puente con solo A de confianza las rechaza');
      const log1 = fs.readFileSync(path.join(dir, 'firma-b.txt'), 'utf8');
      check(/version 0\.21\.3/.test(log1) && !/PRIVATE|BEGIN/.test(log1), 'firma-b.txt solo guarda versión, huellas y huella de la clave');
      r = run(['firmar-agente', dir, pub, hu], env, 'FIRMAR 0.21.3\n');
      check(r.status === 1 && /Ya hay firmas/.test(r.stderr), 'no pisa firmas que ya existen');

      // contenido cambiado después de sacar las huellas
      const d2 = path.join(tmp, 'v2'), h2 = makeVersion(d2, '0.21.3');
      fs.appendFileSync(path.join(d2, 'Vigia-0.21.3-win-x64.zip'), 'x');
      r = run(['firmar-agente', d2, pub, h2], env, 'FIRMAR 0.21.3\n');
      check(r.status === 1 && !fs.existsSync(path.join(d2, 'vigia-manifest.json.sig')), 'zip cambiado tras las huellas: no se firma');
      // hoja de huellas de otra versión
      const d3 = path.join(tmp, 'v3'); makeVersion(d3, '0.21.3');
      r = run(['firmar-agente', d3, pub, hu], env, 'FIRMAR 0.21.3\n');
      check(r.status === 1, 'huellas que no son de estas piezas: no se firma');
      // Setup distinto de Puente
      const d4 = path.join(tmp, 'v4'), h4 = makeVersion(d4, '0.21.3');
      fs.writeFileSync(path.join(d4, 'Vigia-Setup.exe'), 'otro');
      r = run(['firmar-agente', d4, pub, h4], env, 'FIRMAR 0.21.3\n');
      check(r.status === 1, 'Vigia-Setup.exe distinto de Puente.exe: no se firma');
    });

    // ---- 2. fallos del agente
    const pubFrom = async (env, pubSsh, name) => {
      const keys = path.join(tmp, 'k-' + name);
      run(['publica-ssh', fs.readFileSync(pubSsh, 'utf8').trim(), keys, 'labB'], env);
      return path.join(keys, 'vigia-labB.publica.txt');
    };
    for (const [mode, what] of [['rechaza', 'el dueño la rechaza en Bitwarden'], ['otra-clave', 'el agente firma con otra clave'], ['rsa', 'el agente devuelve una firma que no es Ed25519']]) {
      await withAgent(mode, async ({ env, pubSsh }) => {
        const pub = await pubFrom(env, pubSsh, mode);
        const d = path.join(tmp, 'f-' + mode), h = makeVersion(d, '0.21.3');
        const r = run(['firmar-agente', d, pub, h], env, 'FIRMAR 0.21.3\n');
        check(r.status !== 0 && !fs.readdirSync(d).some(f => f.endsWith('.sig')), `${what}: falla y no escribe ningún .sig`);
      });
    }
    await withAgent('sin-claves', async ({ env }) => {
      const other = crypto.generateKeyPairSync('ed25519').publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
      const blob = Buffer.concat([Buffer.from([0, 0, 0, 11]), Buffer.from('ssh-ed25519'), Buffer.from([0, 0, 0, 32]), other]);
      const keys = path.join(tmp, 'k-sin');
      run(['publica-ssh', 'ssh-ed25519 ' + blob.toString('base64'), keys, 'labB'], env);
      const r = run(['agente', path.join(keys, 'vigia-labB.publica.txt')], env);
      check(r.status === 1 && /NO está en el agente/.test(r.stderr), 'bóveda bloqueada / clave ausente: "agente" lo detecta');
      const d = path.join(tmp, 'f-sin'), h = makeVersion(d, '0.21.3');
      const r2 = run(['firmar-agente', d, path.join(keys, 'vigia-labB.publica.txt'), h], env, 'FIRMAR 0.21.3\n');
      check(r2.status !== 0 && !fs.readdirSync(d).some(f => f.endsWith('.sig')), 'clave B no ofrecida por el agente: falla sin escribir nada');
    });
    {
      const env = { ...process.env, VIGIA_AGENTE_PIPE: 'vigia-no-existe-' + crypto.randomBytes(4).toString('hex') };
      const r = run(['agente'], env);
      check(r.status !== 0 && /no hay agente SSH/.test(r.stderr), 'Bitwarden cerrado (sin agente): error claro');
    }
    // ---- 3. con el agente real (sin VIGIA_AGENTE_PIPE) y entrada no interactiva: se niega antes de pedir nada
    {
      const keys = path.join(tmp, 'k-real');
      const B = crypto.generateKeyPairSync('ed25519').publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
      const blob = Buffer.concat([Buffer.from([0, 0, 0, 11]), Buffer.from('ssh-ed25519'), Buffer.from([0, 0, 0, 32]), B]);
      const env = { ...process.env }; delete env.VIGIA_AGENTE_PIPE;
      run(['publica-ssh', 'ssh-ed25519 ' + blob.toString('base64'), keys, 'labX'], env);
      const r = run(['prueba-agente', path.join(keys, 'vigia-labX.publica.txt'), path.join(tmp, 'nunca.txt')], env, 'PRUEBA\n');
      check(r.status === 1 && /no interactiva|la lanzó/.test(r.stderr) && !fs.existsSync(path.join(tmp, 'nunca.txt')), 'agente real desde un proceso automático: se niega sin pedir firma');
    }
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
  console.log(`agente-test: OK=${ok} FAIL=${fail}`);
  process.exit(fail ? 1 : 0);
})();
