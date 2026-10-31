#!/usr/bin/env node
// Comprobación INDEPENDIENTE de las firmas de B (Ed25519 del agente de Bitwarden): usa OpenSSL (node:crypto), no el
// verificador de Puente/VigiaFirma. Solo datos públicos.
//
//   node tools/signing/verificar-b.js <vigia-B.publica.txt> <archivo> [<archivo> ...]   cada archivo con su .sig al lado
//   node tools/signing/verificar-b.js <vigia-B.publica.txt> --prueba <salida.txt>        resultado de VigiaFirma prueba-agente
// Código de salida 0 solo si TODO verifica.
'use strict';
const fs = require('fs'), path = require('path'), crypto = require('crypto');

function die(m) { console.error(m); process.exit(2); }
const [pubFile, ...rest] = process.argv.slice(2);
if (!pubFile || !rest.length) die('uso: node tools/signing/verificar-b.js <publica.txt> <archivo...> | --prueba <salida.txt>');

const t = fs.readFileSync(pubFile, 'utf8');
const field = k => (t.match(new RegExp('^' + k + ' (\\S+)\\s*$', 'm')) || [])[1];
if (!t.startsWith('vigia-clave-publica 1') || !field('publica')) die(pubFile + ' no es una clave pública de Vigía');
const spki = Buffer.from(field('publica'), 'base64');
if (crypto.createHash('sha256').update(spki).digest('hex') !== (field('huella') || '').toLowerCase()) die('la huella anotada no es la de la clave');
const key = crypto.createPublicKey({ key: spki, format: 'der', type: 'spki' });
if (key.asymmetricKeyType !== 'ed25519') die('la clave no es Ed25519');
const raw = spki.subarray(12);
const sshBlob = Buffer.concat([Buffer.from([0, 0, 0, 11]), Buffer.from('ssh-ed25519'), Buffer.from([0, 0, 0, 32]), raw]);
console.log(`clave ${field('nombre')}  SHA256:${crypto.createHash('sha256').update(sshBlob).digest('base64').replace(/=+$/, '')}`);

let bad = 0;
const res = (ok, what) => { console.log((ok ? 'OK   ' : 'FAIL ') + what); if (!ok) bad++; };

if (rest[0] === '--prueba') {
  const r = fs.readFileSync(rest[1], 'utf8');
  const g = k => (r.match(new RegExp('^' + k + ' (\\S+)\\s*$', 'm')) || [])[1];
  const msg = Buffer.from(g('mensaje') || '', 'base64'), sig = Buffer.from(g('firma') || '', 'base64');
  res(r.startsWith('vigia-prueba-agente-resultado 1') && msg.toString().startsWith('vigia-prueba-agente 1\n'), 'es un resultado de prueba de VigiaFirma');
  res(sig.length === 64, 'la firma tiene 64 bytes (Ed25519 pura, sin envoltorio SSH)');
  res(sig.length === 64 && crypto.verify(null, msg, key, sig), 'la firma verifica con la clave (OpenSSL)');
} else {
  for (const f of rest) {
    const name = path.basename(f);
    let st;
    try { st = fs.readFileSync(f + '.sig', 'utf8'); } catch { res(false, name + ': no tiene .sig'); continue; }
    const lines = st.replace(/\r\n/g, '\n').trim().split('\n');
    if (lines.length !== 3 || lines[0] !== 'vigia-firma-ed25519 1' || !lines[1].startsWith('declaracion ') || !lines[2].startsWith('firma ')) { res(false, name + ': el .sig no es una firma de B'); continue; }
    const decl = Buffer.from(lines[1].slice(12), 'base64'), sig = Buffer.from(lines[2].slice(6), 'base64');
    const d = decl.toString('utf8');
    const okFmt = /^vigia-declaracion-ed25519 1\nversion \d+\.\d+\.\d+\n(sha256 [0-9a-f]{64} [A-Za-z0-9._-]+\n)+$/.test(d);
    const sha = crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
    res(okFmt, `${name}: declaración bien formada (${(d.match(/^version (\S+)/m) || [])[1] || '?'})`);
    res(d.split('\n').includes(`sha256 ${sha} ${name}`), `${name}: su SHA-256 ${sha} está en la declaración con su nombre`);
    res(sig.length === 64 && crypto.verify(null, decl, key, sig), `${name}: la declaración está firmada por la clave (OpenSSL)`);
  }
}
console.log(bad ? `${bad} fallo(s): NO vale` : 'todo verifica');
process.exit(bad ? 1 : 0);
