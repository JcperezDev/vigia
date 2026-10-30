#!/usr/bin/env node
// Cambia las claves PÚBLICAS de confianza de Vigía y de Puente a la vez (las dos listas tienen que ser iguales).
// Nunca toca claves privadas: solo edita app/Puente/Program.cs y app/Vigia/Updates.cs.
//
//   node tools/signing/preparar-relevo.js anadir <clave-publica-base64> [raíz]
//       versión puente: sigue confiando en la clave actual y además en la nueva (NextKeys). Se firma con la actual.
//   node tools/signing/preparar-relevo.js promover [raíz]
//       versión siguiente: la clave de relevo pasa a ser la única de confianza (la vieja deja de valer). Se firma con la nueva.
//   node tools/signing/preparar-relevo.js comprobar [raíz]
//       solo mira que las dos listas sean iguales y que cada clave sea P-256 de verdad (rc 0 bien, 1 mal).
//
// La clave pública nueva sale de `Puente.exe --genkey <archivo>` ejecutado en el equipo de firma (lo que imprime).
//
// anadir y promover suben además la versión de Puente (app/Puente/Puente.csproj, el último número). Sin eso, una
// instalación que ya tiene Puente con ese número no lo cambia (Puente solo se sustituye por uno MÁS NUEVO), se queda con
// la lista de claves vieja y, cuando llegue la versión firmada solo con la clave nueva, rechaza todas las actualizaciones.
'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const FILES = ['app/Puente/Program.cs', 'app/Vigia/Updates.cs'];
const RX_PUB = /const string PublicKey = "([^"]*)";/;
const RX_NEXT = /static readonly string\[\] NextKeys = \[([^\]]*)\];/;
const PUENTE_PROJ = 'app/Puente/Puente.csproj';
const RX_PVER = /<Version>(\d+)\.(\d+)\.(\d+)<\/Version>/;

// sube el último número de la versión de Puente; devuelve [antes, después]
function bumpPuente(root) {
  const p = path.join(root, PUENTE_PROJ);
  const src = fs.readFileSync(p, 'utf8');
  const m = src.match(RX_PVER);
  if (!m) throw new Error(`${PUENTE_PROJ}: no encuentro <Version>x.y.z</Version>`);
  const to = `${m[1]}.${m[2]}.${Number(m[3]) + 1}`;
  fs.writeFileSync(p, src.replace(RX_PVER, `<Version>${to}</Version>`));
  return [`${m[1]}.${m[2]}.${m[3]}`, to];
}

function validP256(b64) {
  try {
    const k = crypto.createPublicKey({ key: Buffer.from(b64, 'base64'), format: 'der', type: 'spki' });
    return k.asymmetricKeyType === 'ec' && k.asymmetricKeyDetails.namedCurve === 'prime256v1'
      && Buffer.from(k.export({ format: 'der', type: 'spki' })).toString('base64') === b64;
  } catch { return false; }
}

function read(root) {
  return FILES.map(f => {
    const p = path.join(root, f);
    const src = fs.readFileSync(p, 'utf8');
    const pub = src.match(RX_PUB), next = src.match(RX_NEXT);
    if (!pub || !next) throw new Error(`${f}: no encuentro PublicKey o NextKeys`);
    const list = next[1].split(',').map(s => s.trim()).filter(Boolean).map(s => {
      const m = s.match(/^"([^"]+)"$/); if (!m) throw new Error(`${f}: NextKeys tiene algo que no es una cadena: ${s}`); return m[1];
    });
    return { f, p, src, pub: pub[1], next: list };
  });
}

function problems(st) {
  const out = [];
  const [a, b] = st;
  if (a.pub !== b.pub) out.push('PublicKey distinta en Puente y en Vigía');
  if (JSON.stringify(a.next) !== JSON.stringify(b.next)) out.push('NextKeys distinta en Puente y en Vigía');
  for (const s of st) {
    if (!validP256(s.pub)) out.push(`${s.f}: PublicKey no es una clave P-256 válida`);
    s.next.forEach((k, i) => {
      if (!validP256(k)) out.push(`${s.f}: NextKeys[${i}] no es una clave P-256 válida`);
      if (k === s.pub) out.push(`${s.f}: NextKeys[${i}] repite la clave actual`);
    });
    if (new Set(s.next).size !== s.next.length) out.push(`${s.f}: NextKeys tiene claves repetidas`);
  }
  return out;
}

function write(st, pub, next) {
  for (const s of st) {
    const src = s.src.replace(RX_PUB, `const string PublicKey = "${pub}";`)
      .replace(RX_NEXT, `static readonly string[] NextKeys = [${next.map(k => `"${k}"`).join(', ')}];`);
    fs.writeFileSync(s.p, src);
  }
}

function main(argv) {
  const [cmd, ...rest] = argv;
  const root = path.resolve(cmd === 'anadir' ? (rest[1] || '.') : (rest[0] || '.'));
  const st = read(root);
  const before = problems(st);
  if (cmd === 'comprobar') {
    if (before.length) { before.forEach(p => console.error(p)); return 1; }
    console.log(`bien: ${st[0].next.length} clave(s) de relevo, iguales en Puente y en Vigía`);
    return 0;
  }
  if (before.length) { before.forEach(p => console.error(p)); console.error('Arregla esto antes de cambiar claves.'); return 1; }
  if (cmd === 'anadir') {
    const k = (rest[0] || '').trim();
    if (!validP256(k)) { console.error('Eso no es una clave pública P-256 en base64 (lo que imprime Puente.exe --genkey).'); return 1; }
    if (/PRIVATE|BEGIN/.test(k)) { console.error('Eso parece una clave privada. Aquí solo va la pública.'); return 1; }
    if (k === st[0].pub) { console.error('Esa es la clave actual; el relevo tiene que ser otra.'); return 1; }
    if (st[0].next.includes(k)) { console.log('Esa clave de relevo ya estaba; no cambio nada.'); return 0; }
    write(st, st[0].pub, [...st[0].next, k]);
  } else if (cmd === 'promover') {
    if (st[0].next.length !== 1) { console.error(`Para promover tiene que haber exactamente una clave de relevo (hay ${st[0].next.length}).`); return 1; }
    write(st, st[0].next[0], []);
  } else {
    console.error('Uso: preparar-relevo.js anadir <clave-publica> [raíz] | promover [raíz] | comprobar [raíz]');
    return 2;
  }
  const after = problems(read(root));
  if (after.length) { after.forEach(p => console.error(p)); return 1; }
  const [pv0, pv1] = bumpPuente(root);
  console.log(`Puente ${pv0} -> ${pv1} (para que las instalaciones cambien su Puente y reciban la lista nueva de claves)`);
  console.log(cmd === 'anadir' ? 'listo: Puente y Vigía confían en la clave actual y en la nueva (firmar esta versión con la ACTUAL)'
    : 'listo: la clave nueva es la única de confianza (firmar esta versión con la NUEVA)');
  return 0;
}

if (require.main === module) process.exit(main(process.argv.slice(2)));
module.exports = { validP256, main };
