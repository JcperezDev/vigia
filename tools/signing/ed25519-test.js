#!/usr/bin/env node
// Pruebas del verificador Ed25519 de Puente/Vigía (app/Puente/Ed25519.cs) y del formato de firma de B.
// Solo claves de usar y tirar (generadas aquí en memoria) y los vectores públicos del RFC 8032. Nada real.
//
// Uso: node tools/signing/ed25519-test.js <Puente-test.exe>
'use strict';
const fs = require('fs'), os = require('os'), path = require('path'), crypto = require('crypto');
const { spawnSync } = require('child_process');

const exe = process.argv[2];
if (!exe || !fs.existsSync(exe)) { console.error('uso: node tools/signing/ed25519-test.js <Puente-test.exe>'); process.exit(2); }

let ok = 0, fail = 0;
const check = (cond, name) => { console.log((cond ? 'OK   ' : 'FAIL ') + name); cond ? ok++ : fail++; };
const raw = (pubHex, msgHex, sigHex) => spawnSync(exe, ['--ed25519-raw', pubHex, msgHex || '', sigHex], { encoding: 'utf8' }).status;
// "" no es un argumento hex válido para un mensaje vacío en todas las shells: Puente lo recibe como cadena vacía
const L = (1n << 252n) + 27742317777372353535851937790883648493n;
const P = (1n << 255n) - 19n;
const leHex = (n, len = 32) => { const b = Buffer.alloc(len); for (let i = 0; i < len; i++) { b[i] = Number(n & 0xffn); n >>= 8n; } return b.toString('hex'); };
const hexToLe = h => { let n = 0n; const b = Buffer.from(h, 'hex'); for (let i = b.length - 1; i >= 0; i--) n = (n << 8n) | BigInt(b[i]); return n; };

// ---------------------------------------------------------------- 1. vectores del RFC 8032 (sección 7.1)
const rfc = [
  ['TEST 1 (vacío)', 'd75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a', '',
    'e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b'],
  ['TEST 2 (1 byte)', '3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c', '72',
    '92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00'],
  ['TEST 3 (2 bytes)', 'fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025', 'af82',
    '6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a'],
  ['TEST SHA(abc)', 'ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf',
    'ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f',
    'dc2a4459e7369633a52b1bf277839a00201009a3efbf3ecb69bea2186c26b58909351fc9ac90b3ecfdfbc7c66431e0303dca179c138ac17ad9bef1177331a704'],
];
for (const [n, pub, msg, sig] of rfc) {
  check(raw(pub, msg, sig) === 0, `RFC 8032 ${n}: vale`);
  const bad = Buffer.from(sig, 'hex'); bad[10] ^= 1;
  check(raw(pub, msg, bad.toString('hex')) === 1, `RFC 8032 ${n}: con R cambiado no vale`);
}
// TEST 1024 del RFC: el mensaje es largo; se comprueba con una clave propia del mismo tamaño (abajo) y este:
{
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ed25519');
  const pub = publicKey.export({ format: 'der', type: 'spki' }).subarray(12).toString('hex');
  const msg = crypto.randomBytes(1023);
  check(raw(pub, msg.toString('hex'), crypto.sign(null, msg, privateKey).toString('hex')) === 0, 'mensaje de 1023 bytes firmado por OpenSSL: vale');
}

// ---------------------------------------------------------------- 2. firmas de OpenSSL (node:crypto) con claves de usar y tirar
for (let i = 0; i < 12; i++) {
  const { privateKey, publicKey } = crypto.generateKeyPairSync('ed25519');
  const pub = publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
  const msg = crypto.randomBytes(1 + Math.floor(Math.random() * 300));
  const sig = crypto.sign(null, msg, privateKey);
  const good = raw(pub.toString('hex'), msg.toString('hex'), sig.toString('hex')) === 0;
  const m2 = Buffer.from(msg); m2[m2.length - 1] ^= 0x80;
  const badMsg = raw(pub.toString('hex'), m2.toString('hex'), sig.toString('hex')) === 1;
  const s2 = Buffer.from(sig); s2[40] ^= 4;
  const badS = raw(pub.toString('hex'), msg.toString('hex'), s2.toString('hex')) === 1;
  const other = crypto.generateKeyPairSync('ed25519').publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
  const badKey = raw(other.toString('hex'), msg.toString('hex'), sig.toString('hex')) === 1;
  check(good && badMsg && badS && badKey, `OpenSSL #${i + 1}: buena vale; mensaje, S o clave cambiados no valen`);
}

// ---------------------------------------------------------------- 3. bordes del RFC: maleabilidad, no canónicos, orden pequeño
{
  const [, pub, msg, sig] = rfc[1];
  const S = hexToLe(sig.slice(64));
  check(raw(pub, msg, sig.slice(0, 64) + leHex(S + L)) === 1, 'S + L (firma maleable) no vale');
  check(raw(pub, msg, sig.slice(0, 64) + leHex(L)) === 1, 'S = L no vale');
  // clave pública con y >= p (no canónica): y = p + 1 (equivale a y = 1, el neutro)
  check(raw(leHex(P + 1n), msg, sig) === 1, 'clave pública no canónica (y >= p) no vale');
  // clave pública de orden pequeño: el neutro (y = 1) y el punto de orden 2 (y = -1)
  check(raw(leHex(1n), msg, sig) === 1, 'clave pública neutra (orden 1) no vale');
  check(raw(leHex(P - 1n), msg, sig) === 1, 'clave pública de orden 2 no vale');
  // firma "universal" para la clave neutra: R = neutro, S = 0 (si se aceptara la clave, cualquier mensaje valdría)
  check(raw(leHex(1n), '00', leHex(1n) + leHex(0n)) === 1, 'firma R=neutro, S=0 con clave neutra no vale');
  // R no canónico
  check(raw(pub, msg, leHex(P + 1n) + sig.slice(64)) === 1, 'R no canónico no vale');
  // clave que no es un punto de la curva (y = 2 no tiene x)
  check(raw(leHex(2n), msg, sig) === 1, 'clave que no está en la curva no vale');
  check(raw(pub, msg, sig.slice(0, 126)) === 1, 'firma de 63 bytes no vale');
  check(raw(pub.slice(0, 62), msg, sig) === 1, 'clave de 31 bytes no vale');
}

// ---------------------------------------------------------------- 4. formato de Vigía: declaración firmada + Puente --verify
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'ed25519-test-'));
try {
  const spki = k => k.export({ format: 'der', type: 'spki' }).toString('base64');
  const B = crypto.generateKeyPairSync('ed25519'), C = crypto.generateKeyPairSync('ed25519');
  const A = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const sha = f => crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
  const decl = (version, files) => Buffer.from('vigia-declaracion-ed25519 1\nversion ' + version + '\n' + files.map(f => `sha256 ${sha(f)} ${path.basename(f)}\n`).join(''));
  const sigText = (d, key) => `vigia-firma-ed25519 1\ndeclaracion ${d.toString('base64')}\nfirma ${crypto.sign(null, d, key).toString('base64')}\n`;
  const verify = (file, keys) => spawnSync(exe, ['--verify', file], { encoding: 'utf8', env: { ...process.env, PUENTE_TEST_PUBKEY: keys.join(';') } }).status;

  const f1 = path.join(tmp, 'vigia-manifest.json'), f2 = path.join(tmp, 'Puente.exe');
  fs.writeFileSync(f1, '{"version":"0.21.3"}'); fs.writeFileSync(f2, crypto.randomBytes(5000));
  const d = decl('0.21.3', [f1, f2]);
  const st = sigText(d, B.privateKey);
  fs.writeFileSync(f1 + '.sig', st); fs.writeFileSync(f2 + '.sig', st);
  check(verify(f1, [spki(A.publicKey), spki(B.publicKey)]) === 0, 'formato Vigía: manifiesto firmado con B vale con A+B de confianza');
  check(verify(f2, [spki(A.publicKey), spki(B.publicKey)]) === 0, 'formato Vigía: Puente.exe firmado con B (misma declaración) vale');
  check(verify(f1, [spki(B.publicKey)]) === 0, 'formato Vigía: vale con solo B de confianza');
  check(verify(f1, [spki(A.publicKey)]) === 1, 'firma de B con solo A de confianza no vale');
  check(verify(f1, [spki(A.publicKey), spki(C.publicKey)]) === 1, 'firma de B con otra clave Ed25519 de confianza no vale');
  fs.writeFileSync(f1, '{"version":"0.21.4"}');
  check(verify(f1, [spki(B.publicKey)]) === 1, 'manifiesto cambiado después de firmar no vale');
  fs.writeFileSync(f1, '{"version":"0.21.3"}');
  // declaración cambiada (otra huella) con la firma original
  const d2 = Buffer.from(d.toString().replace('0.21.3', '0.21.9'));
  fs.writeFileSync(f1 + '.sig', `vigia-firma-ed25519 1\ndeclaracion ${d2.toString('base64')}\nfirma ${st.split('\nfirma ')[1]}`);
  check(verify(f1, [spki(B.publicKey)]) === 1, 'declaración cambiada con la firma original no vale');
  // sin prefijo de dominio: aunque esté firmada por B, Puente no la acepta
  const d3 = Buffer.from(d.toString().replace('vigia-declaracion-ed25519 1\n', ''));
  fs.writeFileSync(f1 + '.sig', `vigia-firma-ed25519 1\ndeclaracion ${d3.toString('base64')}\nfirma ${crypto.sign(null, d3, B.privateKey).toString('base64')}\n`);
  check(verify(f1, [spki(B.publicKey)]) === 1, 'declaración sin el prefijo de dominio (firmada por B) no vale');
  // B firmando directamente el archivo (sin declaración) en base64 crudo: no se confunde con el formato de A
  fs.writeFileSync(f1 + '.sig', crypto.sign(null, fs.readFileSync(f1), B.privateKey).toString('base64'));
  check(verify(f1, [spki(A.publicKey), spki(B.publicKey)]) === 1, 'firma Ed25519 cruda (sin declaración) no vale');
  // A sigue funcionando igual con B también de confianza
  fs.writeFileSync(f1 + '.sig', crypto.sign('sha256', fs.readFileSync(f1), { key: A.privateKey, dsaEncoding: 'ieee-p1363' }).toString('base64'));
  check(verify(f1, [spki(A.publicKey), spki(B.publicKey)]) === 0, 'firma ECDSA de A sigue valiendo con A+B de confianza');
  check(verify(f1, [spki(B.publicKey)]) === 1, 'firma de A con solo B de confianza no vale');
  // un .sig de B con la declaración de otro archivo
  const f3 = path.join(tmp, 'otro.bin'); fs.writeFileSync(f3, 'otro');
  fs.writeFileSync(f3 + '.sig', st);
  check(verify(f3, [spki(B.publicKey)]) === 1, 'archivo que no está en la declaración no vale');
  // línea extra en la declaración
  const d4 = Buffer.concat([d, Buffer.from('extra 1\n')]);
  fs.writeFileSync(f1 + '.sig', sigText(d4, B.privateKey));
  check(verify(f1, [spki(B.publicKey)]) === 1, 'declaración con una línea desconocida no vale');
  // --trusted-keys: lista tipo y huella de cada clave de confianza (lo usa firmar-fuera.ps1 -RelevoPublica)
  const hk = k => crypto.createHash('sha256').update(Buffer.from(spki(k), 'base64')).digest('hex');
  const tk = spawnSync(exe, ['--trusted-keys'], { encoding: 'utf8', env: { ...process.env, PUENTE_TEST_PUBKEY: [spki(A.publicKey), spki(B.publicKey)].join(';') } });
  check(tk.status === 0 && tk.stdout.split(/\r?\n/).filter(Boolean).join('|') === `P-256 ${hk(A.publicKey)}|Ed25519 ${hk(B.publicKey)}`, '--trusted-keys lista A (P-256) y B (Ed25519) con su huella');
  // preparar-relevo acepta B Ed25519 y rechaza una Ed25519 con codificación de más
  const relevo = require('./preparar-relevo.js');
  check(relevo.validKey(spki(B.publicKey)) && relevo.keyType(spki(B.publicKey)) === 'Ed25519' && relevo.keyType(spki(A.publicKey)) === 'P-256', 'preparar-relevo reconoce P-256 y Ed25519');
  check(!relevo.validKey(Buffer.concat([Buffer.from(spki(B.publicKey), 'base64'), Buffer.from([0])]).toString('base64')), 'preparar-relevo rechaza una clave Ed25519 con bytes de más');
  check(!relevo.validKey(crypto.generateKeyPairSync('ed448').publicKey.export({ format: 'der', type: 'spki' }).toString('base64')), 'preparar-relevo rechaza Ed448');
} finally {
  fs.rmSync(tmp, { recursive: true, force: true });
}

console.log(`ed25519-test: OK=${ok} FAIL=${fail}`);
process.exit(fail ? 1 : 0);
