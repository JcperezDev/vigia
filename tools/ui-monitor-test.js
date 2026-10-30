// Uso: node tools/ui-monitor-test.js <chrome.exe> <ruta ABSOLUTA de app/Vigia/wwwroot> <carpeta de salida>
// Prueba del Monitor en Chrome headless (sin ventana) con un WebView simulado: no toca Windows ni sensores.
// Comprueba que sin lectura fiable del procesador se muestra «Temperatura de CPU no disponible», nunca 0 °C ni un
// valor inventado, en los cinco idiomas, y que una lectura real sí se ve con su unidad.
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const [, , chrome, wwwroot, outDir] = process.argv;
if (!chrome || !wwwroot || !outDir || !path.isAbsolute(wwwroot)) { console.error('uso: ui-monitor-test.js <chrome.exe> <wwwroot absoluto> <salida>'); process.exit(2); }
const PORT = 9348;
const NA = { es: 'Temperatura de CPU no disponible.', en: 'CPU temperature not available.', pt: 'Temperatura da CPU indisponível.',
  fr: 'Température CPU indisponible.', de: 'CPU-Temperatur nicht verfügbar.' };

let id = 0; const pending = new Map(); let ws;
const cdp = (method, params = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params })); });
const errors = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const ev = async (expr) => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails).slice(0, 500)); return r.result.value; };
const results = []; let fails = 0;
const check = (ok, name, extra = '') => { results.push(`${ok ? 'OK  ' : 'FAIL'} ${name}${extra ? ` (${extra})` : ''}`); if (!ok) fails++; };
const tele = (temp, gpuTemp = null) => ({ ts: Date.now(), elevated: false, notes: [],
  cpu: { name: 'Procesador de prueba', temp, load: 12, clockGhz: 3.1, cores: 8, threads: 16 },
  gpu: { name: 'GPU de prueba', temp: gpuTemp, load: 3, coreClockMhz: 500, vramUsedGb: 1, vramTotalGb: 8 },
  ram: { type: 'DDR5', speedMts: 5600, usedGb: 10, totalGb: 32, cl: 30, temp: null }, drives: [] });
const emit = (payload) => ev(`window.__emit('telemetry', ${JSON.stringify(payload)})`);
const card = `(() => { const t = document.getElementById('cpu-temp'); const p = t.parentElement; const u = p.querySelector('small');
  const na = document.getElementById('cpu-temp-na');
  return { text: t.textContent.trim(), unitShown: !!u && !u.hidden && getComputedStyle(u).display !== 'none',
    na: !na.hidden && getComputedStyle(na).display !== 'none', naText: na.textContent.trim(), naClass: p.classList.contains('na') }; })()`;

(async () => {
  fs.mkdirSync(outDir, { recursive: true });
  const prof = path.join(outDir, 'chrome-prof-monitor'); fs.rmSync(prof, { recursive: true, force: true });
  const ch = spawn(chrome, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${prof}`, '--no-first-run', '--no-default-browser-check',
    '--disable-extensions', '--allow-file-access-from-files', '--disable-background-networking', '--disable-sync', '--window-size=1400,1000', 'about:blank'], { stdio: 'ignore', windowsHide: true });
  try {
    let tgt;
    for (let i = 0; i < 50 && !tgt; i++) { await sleep(200); try { const l = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json(); tgt = l.find((t) => t.type === 'page'); } catch { /* aún no */ } }
    ws = new WebSocket(tgt.webSocketDebuggerUrl);
    await new Promise((r) => ws.addEventListener('open', r));
    ws.addEventListener('message', (m) => {
      const d = JSON.parse(m.data);
      if (d.id && pending.has(d.id)) { const p = pending.get(d.id); pending.delete(d.id); d.error ? p.rej(new Error(d.error.message)) : p.res(d.result); }
      else if (d.method === 'Runtime.exceptionThrown') errors.push(d.params.exceptionDetails.exception?.description || d.params.exceptionDetails.text);
    });
    await cdp('Runtime.enable'); await cdp('Page.enable');
    const url = 'file:///' + path.join(wwwroot, 'index.html').replace(/\\/g, '/');
    for (const lang of Object.keys(NA)) {
      await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `
        (() => { const ls = []; window.__sent = [];
          try { localStorage.setItem('vigia-lang', ${JSON.stringify(lang)}); } catch {}
          window.chrome = window.chrome || {};
          window.chrome.webview = { postMessage: (m) => window.__sent.push(m), addEventListener: (t, f) => { if (t === 'message') ls.push(f); }, removeEventListener() {} };
          window.__emit = (type, payload) => ls.forEach((f) => f({ data: { type, payload } }));
        })();` });
      await cdp('Page.navigate', { url }); await sleep(1200);

      for (const [what, v] of [['sin lectura (null)', null], ['sensor sin driver (0)', 0], ['negativo', -5], ['absurdo (200)', 200]]) {
        await emit(tele(v)); await sleep(150);
        const c = await ev(card);
        check(c.text !== '0' && !/\d/.test(c.text), `${lang}: ${what}: no muestra número`, c.text);
        check(!c.unitShown, `${lang}: ${what}: sin «°C»`);
        check(c.na && c.naText === NA[lang], `${lang}: ${what}: «${NA[lang]}»`, c.naText);
      }
      await emit(tele(54.6)); await sleep(150);
      let c = await ev(card);
      check(/^5[45]/.test(c.text) && c.unitShown && !c.na && !c.naClass, `${lang}: lectura real 54,6: se ve con °C y sin aviso`, JSON.stringify(c));
      await emit(tele(null)); await sleep(150);
      c = await ev(card);
      check(c.na && !c.unitShown, `${lang}: si la lectura se pierde vuelve el aviso`);
    }
    check(errors.length === 0, 'sin errores de JavaScript', errors.slice(0, 3).join(' | '));
  } catch (e) { check(false, 'excepción en la prueba', String(e && e.stack || e)); }
  finally { try { ws && ws.close(); } catch { } ch.kill(); }
  console.log(results.join('\n'));
  console.log(`UI monitor OK=${results.length - fails} FAIL=${fails}`);
  process.exit(fails ? 1 : 0);
})();
