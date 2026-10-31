#!/usr/bin/env node
// Agente SSH de LABORATORIO para probar VigiaFirma sin Bitwarden: escucha en \\.\pipe\<nombre> y firma con una clave
// Ed25519 de usar y tirar que se crea en memoria al arrancar. Nunca toca claves reales.
//
//   node tools/signing/agente-lab.js <nombre-tubería> <modo> <archivo-publica-ssh>
// Modos: ok | rechaza (responde FAILURE como cuando el dueño pulsa "Denegar") | otra-clave (firma con otra clave) |
//        rsa (devuelve un tipo de firma distinto) | mudo (no contesta: aprobación que nunca llega) | sin-claves
// Escribe en <archivo-publica-ssh> la línea "ssh-ed25519 AAAA... lab" de su clave y registra cada petición en stdout.
'use strict';
const net = require('net'), fs = require('fs'), crypto = require('crypto');
const [name, mode, pubOut] = process.argv.slice(2);
if (!name || !mode || !pubOut) { console.error('uso: agente-lab.js <tubería> <modo> <publica-ssh>'); process.exit(2); }

const k = crypto.generateKeyPairSync('ed25519'), other = crypto.generateKeyPairSync('ed25519');
const raw = k.publicKey.export({ format: 'der', type: 'spki' }).subarray(12);
const s = b => { const h = Buffer.alloc(4); h.writeUInt32BE(b.length); return Buffer.concat([h, b]); };
const blob = Buffer.concat([s(Buffer.from('ssh-ed25519')), s(raw)]);
fs.writeFileSync(pubOut, 'ssh-ed25519 ' + blob.toString('base64') + ' lab\n');

const frame = b => { const h = Buffer.alloc(4); h.writeUInt32BE(b.length); return Buffer.concat([h, b]); };
const server = net.createServer(c => {
  let buf = Buffer.alloc(0);
  c.on('error', () => {});
  c.on('data', d => {
    buf = Buffer.concat([buf, d]);
    while (buf.length >= 4 && buf.length >= 4 + buf.readUInt32BE(0)) {
      const n = buf.readUInt32BE(0), m = buf.subarray(4, 4 + n); buf = buf.subarray(4 + n);
      const t = m[0];
      console.log('peticion ' + t);
      if (t === 11) {
        if (mode === 'sin-claves') { c.write(frame(Buffer.from([12, 0, 0, 0, 0]))); continue; }
        c.write(frame(Buffer.concat([Buffer.from([12, 0, 0, 0, 1]), s(blob), s(Buffer.from('lab'))])));
      } else if (t === 13) {
        let o = 1; const rd = () => { const l = m.readUInt32BE(o); const v = m.subarray(o + 4, o + 4 + l); o += 4 + l; return v; };
        const kb = rd(), data = rd();
        console.log('firma pedida: ' + data.length + ' bytes, sha256 ' + crypto.createHash('sha256').update(data).digest('hex'));
        if (mode === 'mudo') continue;
        if (mode === 'rechaza' || !kb.equals(blob)) { c.write(frame(Buffer.from([5]))); continue; }
        const key = mode === 'otra-clave' ? other.privateKey : k.privateKey;
        const sig = crypto.sign(null, data, key);
        const alg = mode === 'rsa' ? 'rsa-sha2-256' : 'ssh-ed25519';
        c.write(frame(Buffer.concat([Buffer.from([14]), s(Buffer.concat([s(Buffer.from(alg)), s(sig)]))])));
      } else c.write(frame(Buffer.from([5])));
    }
  });
});
server.listen('\\\\.\\pipe\\' + name, () => console.log('listo'));
