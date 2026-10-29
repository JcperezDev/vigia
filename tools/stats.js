// Cuántas veces se ha descargado Vigía, con los contadores públicos de GitHub. La app no manda nada:
// no hay rastreo, cuentas ni servidor propio.
//   Vigia-Setup.exe  descargas desde la web o el enlace compartido (gente nueva o que reinstala)
//   Puente.exe       la web hasta v0.15.0 y, después, Vigía cuando le falta su instalador
//   Vigia-*.zip      instalaciones y actualizaciones de cada versión (una por PC y versión)
// GitHub cuenta descargas, no personas: quien descarga dos veces cuenta dos.
// Uso: node tools/stats.js [--json]
'use strict';
const https = require('https');
const get = (url) => new Promise((ok, ko) => {
  https.get(url, { headers: { 'User-Agent': 'vigia-stats', Accept: 'application/vnd.github+json' } }, (r) => {
    let b = ''; r.on('data', (c) => (b += c));
    r.on('end', () => (r.statusCode === 200 ? ok(JSON.parse(b)) : ko(new Error(`GitHub ${r.statusCode}: ${b.slice(0, 200)}`))));
  }).on('error', ko);
});

(async () => {
  // las versiones están en JcperezDev/vigia; vigia-releases es la copia para instalaciones viejas.
  // Las descargas de la misma versión se suman.
  const byTag = new Map();
  for (const repo of ['JcperezDev/vigia', 'JcperezDev/vigia-releases']) {
    for (let page = 1; ; page++) {
      const r = await get(`https://api.github.com/repos/${repo}/releases?per_page=100&page=${page}`);
      for (const x of r) {
        const prev = byTag.get(x.tag_name);
        if (!prev) byTag.set(x.tag_name, { ...x, assets: x.assets.map((a) => ({ ...a })) });
        else for (const a of x.assets) { const p = prev.assets.find((b) => b.name === a.name); if (p) p.download_count += a.download_count; else prev.assets.push({ ...a }); }
      }
      if (r.length < 100) break;
    }
  }
  const rels = [...byTag.values()];
  const rows = rels.map((r) => {
    const n = (re) => r.assets.filter((a) => re.test(a.name)).reduce((s, a) => s + a.download_count, 0);
    return { version: r.tag_name, date: r.published_at.slice(0, 10), setup: n(/^Vigia-Setup\.exe$/), puente: n(/^Puente\.exe$/), zip: n(/^Vigia-.*\.zip$/) };
  }).sort((a, b) => { const v = (s) => s.replace(/^v/, '').split('.').map(Number); const x = v(a.version), y = v(b.version); return x[0] - y[0] || x[1] - y[1] || x[2] - y[2]; });
  const tot = rows.reduce((t, r) => ({ setup: t.setup + r.setup, puente: t.puente + r.puente, zip: t.zip + r.zip }), { setup: 0, puente: 0, zip: 0 });
  if (process.argv.includes('--json')) { console.log(JSON.stringify({ when: new Date().toISOString(), total: tot, versions: rows }, null, 1)); return; }
  const pad = (s, w) => String(s).padStart(w);
  console.log(`${'versión'.padEnd(9)} ${'fecha'.padEnd(10)} ${pad('desde web', 9)} ${pad('Puente', 7)} ${pad('instal./act.', 12)}`);
  for (const r of rows) console.log(`${r.version.padEnd(9)} ${r.date} ${pad(r.setup, 9)} ${pad(r.puente, 7)} ${pad(r.zip, 12)}`);
  console.log(`${'total'.padEnd(20)} ${pad(tot.setup, 9)} ${pad(tot.puente, 7)} ${pad(tot.zip, 12)}`);
  console.log(`\nDescargas del instalador: ${tot.setup + tot.puente}  ·  Instalaciones y actualizaciones: ${tot.zip}`);
  const last = rows[rows.length - 1];
  if (last) console.log(`En la última versión (${last.version}): ${last.zip} PCs la instalaron o se actualizaron a ella.`);
})().catch((e) => { console.error(e.message); process.exit(1); });
