// Prueba el traductor de wwwroot/i18n.js fuera de la app: carga cada diccionario, traduce todas las claves
// y textos de ejemplo, y avisa si algo lanza error o queda a medias.
// Uso: node tools/i18n-check.js [idioma] ["texto a probar" …]
'use strict';
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const www = path.join(__dirname, '..', 'app', 'Vigia', 'wwwroot');
const code = fs.readFileSync(path.join(www, 'i18n.js'), 'utf8');

function load(lang) {
  const ctx = {
    window: {}, navigator: { language: 'es-ES' }, localStorage: { getItem: () => lang },
    document: { body: null, documentElement: {}, addEventListener() {}, createTreeWalker() {} },
    MutationObserver: class { observe() {} }, Event: class {}, NodeFilter: {},
    XMLHttpRequest: class {
      open(_m, u) { this.u = u; }
      send() { this.status = 200; this.responseText = fs.readFileSync(path.join(www, this.u), 'utf8'); }
    },
  };
  ctx.window.dispatchEvent = () => {};
  vm.createContext(ctx);
  vm.runInContext(code, ctx);
  ctx.window.vigiaI18n.set(lang);
  return ctx.window.vigiaI18n;
}

const langs = process.argv[2] ? [process.argv[2]] : ['en', 'pt', 'fr', 'de'];
const extra = process.argv.slice(3);
const keys = JSON.parse(fs.readFileSync(path.join(www, 'i18n', 'strings.json'), 'utf8'));
// same.json: textos que a propósito no se traducen (nombres de juegos y marcas, patrones que se buscan en los
// eventos de Windows, líneas de registro, órdenes). Solo valen si de verdad salen iguales; no es para tapar faltas.
const same = new Set(JSON.parse(fs.readFileSync(path.join(www, 'i18n', 'same.json'), 'utf8')));
const stale = [...same].filter((k) => !keys.includes(k));
if (stale.length) console.log(`  same.json: ${stale.length} textos que ya no existen (p. ej. ${JSON.stringify(stale[0])})`);
let fail = 0;
for (const l of langs) {
  const I = load(l);
  const d = JSON.parse(fs.readFileSync(path.join(www, 'i18n', `${l}.json`), 'utf8'));
  let errs = 0, wrong = 0;
  for (const k of keys) {
    try { const t = I.t(k); if (t !== (k in d || !same.has(k) ? d[k] : k)) { wrong++; if (wrong <= 5) console.log(`  ${l} distinto: ${JSON.stringify(k)} -> ${JSON.stringify(t)}`); } }
    catch (e) { errs++; if (errs <= 5) console.log(`  ${l} error en ${JSON.stringify(k)}: ${e.message}`); }
  }
  for (const s of extra) console.log(`  ${l}: ${s} -> ${I.t(s)}`);
  console.log(`${l}: ${keys.length} claves, ${errs} errores, ${wrong} distintas`);
  fail += errs + wrong;
}
process.exit(fail || stale.length ? 1 : 0);
