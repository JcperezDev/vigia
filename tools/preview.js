// Genera una copia de wwwroot con un mock de chrome.webview alimentado por probe.json, para capturas con Edge headless.
// Uso: node preview.js <wwwroot> <probe.json> <outDir>
const fs = require('fs'), path = require('path');
const [src, probe, out] = process.argv.slice(2);
fs.rmSync(out, { recursive: true, force: true });
fs.cpSync(src, out, { recursive: true });
const p = JSON.parse(fs.readFileSync(probe, 'utf8'));
const mock = `
(() => {
  const snap = ${JSON.stringify(p.snapshot)};
  const clean = ${JSON.stringify(p.clean)};
  const ls = [];
  const emit = (type, payload) => ls.forEach((f) => f({ data: { type, payload } }));
  window.chrome = { webview: {
    addEventListener: (_, f) => ls.push(f),
    postMessage: (m) => { if (m.cmd === 'cleanScan') setTimeout(() => emit('cleanScan', clean), 10); }
  } };
  window.addEventListener('load', () => {
    const t0 = Date.now();
    for (let i = 60; i >= 0; i--) {
      const s = structuredClone(snap); s.ts = t0 - i * 1000;
      s.cpu.temp = snap.cpu.temp + Math.sin(i / 5) * 4; s.cpu.load = Math.max(2, snap.cpu.load + Math.cos(i / 3) * 6);
      if (s.gpu && s.gpu.temp != null) s.gpu.temp = snap.gpu.temp + Math.sin(i / 7) * 3;
      emit('telemetry', s);
    }
    if (location.hash === '#storage') {
      document.querySelector('[data-view=storage]').click();
      document.getElementById('scan').click();
    }
  });
})();`;
fs.writeFileSync(path.join(out, 'mock.js'), mock);
const html = fs.readFileSync(path.join(out, 'index.html'), 'utf8').replace('<script src="app.js">', '<script src="mock.js"></script><script src="app.js">');
fs.writeFileSync(path.join(out, 'index.html'), html);
console.log('ok', out);
