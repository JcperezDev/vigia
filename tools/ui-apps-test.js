// Uso: node tools/ui-apps-test.js <chrome.exe> <app/Vigia/wwwroot> <apps.json de --apps-scan> <Vigia.exe> <carpeta VIGIA_DATA> <carpeta de salida>
// Abre la vista Apps en Chrome sin ventana con un WebView falso. No quita nada: el plan sale de --apps-plan (solo lectura).
// Prueba de la vista Apps en Chrome headless (sin ventana) con un WebView simulado.
// No toca Windows: el "backend" es este script; el plan sale de Vigia.exe --apps-plan (solo lectura).
'use strict';
const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const [, , chrome, wwwroot, scanFile, exe, dataDir, outDir] = process.argv;
const PORT = 9347;
const scan = JSON.parse(fs.readFileSync(scanFile, 'utf8').replace(/^\uFEFF/, ''));

// Este PC ya está limpio: se añaden apps simuladas (claramente falsas) para ejercitar opcionales/útiles/OEM/aprovisionadas.
const fake = (o) => ({ kind: 'appx', publisher: 'Microsoft Corporation', version: '1.0.0.0', arch: 'X64', origin: 'microsoft', why: '', detail: '',
  risk: 'bajo', me: true, others: 0, provisioned: false, deps: [], dependents: [], recovery: 'B', recoveryText: 'Se reinstala desde Microsoft Store si sigue publicada. Sus datos no vuelven.',
  minMode: 'safe', blocked: null, recommended: false, catalogId: null, returned: false, msi: null, scope: '', sizeKb: 0, ...o,
  family: `${o.name}_8wekyb3d8bbwe`, key: `${o.name}_1.0.0.0_x64__8wekyb3d8bbwe`, display: o.display || o.name });
const sim = [
  fake({ name: 'Microsoft.BingNews', display: 'Noticias', category: 'optional', recommended: true }),
  fake({ name: 'Microsoft.BingWeather', display: 'El tiempo', category: 'optional', recommended: true, provisioned: true }),
  fake({ name: 'Microsoft.Copilot', display: 'Copilot', category: 'useful', minMode: 'custom', risk: 'medio' }),
  fake({ name: 'Fabrikam.Promo', display: 'Promo OEM', category: 'oem', origin: 'oem', publisher: 'Fabrikam', minMode: 'advanced', risk: 'alto' }),
  fake({ name: 'Microsoft.GetHelp', display: 'Solo aprovisionada', category: 'optional', me: false, provisioned: true }),
];
const data = { ...scan, apps: [...sim, ...scan.apps] };

let id = 0; const pending = new Map(); let ws;
const cdp = (method, params = {}) => new Promise((res, rej) => { const i = ++id; pending.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params })); });
const errors = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const ev = async (expr) => { const r = await cdp('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }); if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails).slice(0, 500)); return r.result.value; };
const results = []; let fails = 0;
const check = (ok, name, extra = '') => { results.push(`${ok ? 'OK  ' : 'FAIL'} ${name}${extra ? ` (${extra})` : ''}`); if (!ok) fails++; };
const emit = (type, payload) => ev(`window.__emit(${JSON.stringify(type)}, ${JSON.stringify(payload)})`);
const sent = () => ev('window.__sent.splice(0)');

(async () => {
  const prof = path.join(outDir, 'chrome-prof'); fs.rmSync(prof, { recursive: true, force: true });
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
      else if (d.method === 'Runtime.consoleAPICalled' && d.params.type === 'error') errors.push(d.params.args.map((a) => a.value ?? a.description).join(' '));
    });
    await cdp('Runtime.enable'); await cdp('Page.enable');
    await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `
      (() => { const ls = []; window.__sent = [];
        try { localStorage.setItem('vigia-lang', window.__lang || 'es'); } catch {}
        window.chrome = window.chrome || {};
        window.chrome.webview = { postMessage: (m) => window.__sent.push(m), addEventListener: (t, f) => { if (t === 'message') ls.push(f); }, removeEventListener() {} };
        window.__emit = (type, payload) => ls.forEach((f) => f({ data: { type, payload } }));
      })();` });
    const url = 'file:///' + path.join(wwwroot, 'index.html').replace(/\\/g, '/');
    await cdp('Page.navigate', { url }); await sleep(1500);

    // 1. abrir la vista pide el inventario (no lo inventa)
    await sent();
    await ev(`document.querySelector('.nav[data-view="apps"]').click()`); await sleep(200);
    let s = await sent();
    check(s.some((m) => m.cmd === 'appsScan'), 'abrir Apps pide appsScan', JSON.stringify(s.map((m) => m.cmd)));
    check(await ev(`!document.getElementById('view-apps').hidden`), 'la vista Apps se ve');

    // 2. fallo de lectura: aviso, no lista vacía
    await emit('apps', { ok: false, error: 'Windows tardó más de 90 s en dar la lista de apps.', apps: [], win32: [], history: [] });
    check(await ev(`document.getElementById('ap-list').textContent.includes('No pude leer las apps')`), 'fallo de lectura = aviso, no lista vacía');
    check(await ev(`document.getElementById('ap-export').disabled`), 'exportar desactivado si no se pudo leer');

    // 3. inventario real de este PC + simuladas
    const t0 = Date.now(); await emit('apps', data); const tRender = Date.now() - t0;
    const total = data.apps.length + data.win32.length;
    const st = await ev(`[...document.querySelectorAll('#ap-stats dd')].map(x => x.textContent)`);
    check(st[0] === String(total), 'resumen: total detectadas', `${st[0]} vs ${total}`);
    check(st[2] === String([...data.apps, ...data.win32].filter((a) => a.category === 'protected').length), 'resumen: protegidas', st[2]);
    const rows = await ev(`document.querySelectorAll('#ap-list li.ap-row').length`);
    check(rows === Math.min(total, 200), 'filas dibujadas', `${rows}`);
    check(await ev(`[...document.querySelectorAll('#ap-list li.ap-row')].filter(li => li.querySelector('.ap-cat-protected')).every(li => li.querySelector('input').disabled)`), 'protegidas: casilla desactivada');
    check(await ev(`[...document.querySelectorAll('#ap-list input[type=checkbox]')].every(c => !c.checked)`), 'nada viene marcado de entrada');
    const cop = 'Microsoft.Copilot_1.0.0.0_x64__8wekyb3d8bbwe';
    check(await ev(`document.getElementById(${JSON.stringify('ap-' + cop)}).disabled`), 'modo seguro: Copilot (útil) no se puede marcar');
    check(await ev(`document.querySelectorAll('#ap-filters button').length`) === 9, 'nueve filtros');

    // 4. «Recomendadas» en modo seguro: solo opcionales recomendadas de tu usuario
    await ev(`document.getElementById('ap-rec').click()`); await sleep(50);
    const bar = await ev(`document.getElementById('ap-bar-t').textContent`);
    check(/^2 seleccionadas$/.test(bar), 'recomendadas marca solo Noticias y El tiempo', bar);
    check(await ev(`!document.getElementById(${JSON.stringify('ap-' + sim[4].key)}).checked`), 'aprovisionada sin estar en tu usuario: no se marca en seguro');

    // 5. marcar no ejecuta nada
    s = await sent();
    check(!s.some((m) => ['appsRun', 'bloatRemove'].includes(m.cmd)), 'marcar casillas no ejecuta nada', JSON.stringify(s.map((m) => m.cmd)));

    // 6. buscador sobre la lista cargada (sin pedir nada al backend)
    await ev(`(() => { const q = document.getElementById('ap-q'); q.value = 'noticias'; q.dispatchEvent(new Event('input')); })()`); await sleep(300);
    const qr = await ev(`[...document.querySelectorAll('#ap-list li.ap-row strong')].map(x => x.textContent)`);
    check(qr.length === 1 && qr[0] === 'Noticias', 'buscar «noticias»', JSON.stringify(qr));
    s = await sent(); check(s.length === 0, 'buscar no consulta al backend', JSON.stringify(s));
    await ev(`(() => { const q = document.getElementById('ap-q'); q.value = 'zzzz-no'; q.dispatchEvent(new Event('input')); })()`); await sleep(300);
    check(await ev(`document.getElementById('ap-list').textContent.includes('Nada coincide')`), 'búsqueda sin resultados lo dice');
    await ev(`(() => { const q = document.getElementById('ap-q'); q.value = ''; q.dispatchEvent(new Event('input')); })()`); await sleep(300);

    // 7. vista previa obligatoria con el plan real del backend (Vigia.exe --apps-plan, solo lectura)
    await ev(`document.getElementById('ap-review').click()`); await sleep(50);
    s = await sent(); const prev = s.find((m) => m.cmd === 'appsPreview');
    check(!!prev && prev.mode === 'safe' && prev.keys.length === 2 && prev.deprovision.length === 0, 'revisar manda solo claves y modo', JSON.stringify(prev));
    const realKeys = [...data.apps.filter((a) => !a.key.includes('_8wekyb3d8bbwe') || !sim.some((x) => x.key === a.key)).slice(0, 2).map((a) => a.key), ...prev.keys];
    const planOut = path.join(outDir, 'plan-ui.json');
    execFileSync(exe, ['--apps-plan', realKeys.join('|'), planOut, 'safe'], { env: { ...process.env, VIGIA_DATA: dataDir }, windowsHide: true });
    const plan = JSON.parse(fs.readFileSync(planOut, 'utf8').replace(/^\uFEFF/, ''));
    await emit('appsPlan', plan); await sleep(50);
    check(await ev(`!document.getElementById('ap-plan').hidden`), 'se muestra el plan');
    check(await ev(`document.querySelectorAll('#ap-sum dd').length`) === 10, 'resumen del plan con 10 datos');
    check(await ev(`document.getElementById('ap-go').disabled`), '«Quitar ahora» bloqueado hasta aceptar');
    check(await ev(`document.querySelectorAll('#ap-plan-list li').length`) === plan.items.length, 'una fila por app del plan', String(plan.items.length));
    const okN = plan.items.filter((i) => i.ok).length;
    check(okN === 0 ? await ev(`document.getElementById('ap-agree').disabled`) : true, 'si nada se puede quitar no se puede aceptar', `ok=${okN}`);
    // Simulado: un plan con una app lista (el backend real de este PC no tiene opcionales instaladas)
    const fplan = { token: 'abc123', mode: 'safe', changed: false, summary: { selected: 1, ready: 1, protected: 0, unsupported: 0, admin: 0, users: 'Solo tu usuario', deprovision: 1, noAutoRestore: 0, running: 0, restart: 'No hace falta reiniciar.' },
      items: [{ key: sim[1].key, display: 'El tiempo', ok: true, action: 'remove-user+deprovision', problem: null, keep: '', consequences: ['Se borran sus datos locales en tu usuario (ajustes, archivos sin sincronizar).'], recovery: 'B', recoveryText: 'x', admin: true, running: [], users: 'Tu usuario y los usuarios nuevos', dependents: [], publisher: 'Microsoft Corporation', version: '1.0.0.0' }] };
    await emit('appsPlan', fplan); await sleep(50);
    check(!(await ev(`document.getElementById('ap-agree2-row').hidden`)), 'aprovisionamiento: segunda confirmación aparte');
    await ev(`document.getElementById('ap-agree').click()`);
    check(await ev(`document.getElementById('ap-go').disabled`), 'con una sola casilla sigue bloqueado si hay aprovisionamiento');
    await ev(`document.getElementById('ap-agree2').click()`);
    check(!(await ev(`document.getElementById('ap-go').disabled`)), 'con las dos casillas se habilita');
    await ev(`document.getElementById('ap-go').click()`); await sleep(50);
    s = await sent(); const run = s.find((m) => m.cmd === 'appsRun');
    check(!!run && run.token === 'abc123', 'ejecutar manda el token del plan visto', JSON.stringify(run));
    check(!(await ev(`document.getElementById('ap-stop').hidden`)), 'botón cancelar visible durante la ejecución');
    await ev(`document.getElementById('ap-stop').click()`); s = await sent();
    check(s.some((m) => m.cmd === 'appsCancel'), 'cancelar manda appsCancel');
    await emit('appsStep', { key: sim[1].key, state: 'removed', error: null });
    await emit('appsDone', { changed: false, steps: [{ key: sim[1].key, state: 'removed' }], removed: 1, failed: 0, skipped: 0, notStarted: 0, stopped: false, stopReason: null });
    check(await ev(`document.getElementById('ap-run-result').textContent.startsWith('1 hecha')`), 'resumen final', await ev(`document.getElementById('ap-run-result').textContent`));
    // inventario cambiado entre vista previa y ejecución
    await emit('appsDone', { changed: true, newPlan: fplan, steps: [] });
    check(await ev(`document.getElementById('ap-run-result').textContent.includes('cambiaron')`), 'inventario cambiado: plan nuevo y nada ejecutado');

    // 8. modo avanzado pide confirmar; Copilot y OEM se pueden marcar, protegidas no
    await ev(`document.querySelector('#ap-mode [data-mode="advanced"]').click()`); await sleep(100);
    check(await ev(`document.getElementById('ask').open`), 'avanzado pide confirmación');
    await ev(`document.getElementById('ask-yes').click()`); await sleep(100);
    check(await ev(`document.querySelector('#ap-mode [data-mode="advanced"]').getAttribute('aria-checked')`) === 'true', 'modo avanzado activo');
    check(!(await ev(`document.getElementById(${JSON.stringify('ap-' + cop)}).disabled`)), 'avanzado: Copilot se puede elegir a mano');
    check(await ev(`[...document.querySelectorAll('#ap-list li.ap-row')].filter(li => li.querySelector('.ap-cat-protected')).every(li => li.querySelector('input').disabled)`), 'avanzado: protegidas siguen bloqueadas');
    await ev(`document.getElementById(${JSON.stringify('ap-' + sim[1].key)}).click()`); await sleep(50);
    check(await ev(`!!document.querySelector('.ap-deprov input')`), 'avanzado: casilla aparte para el aprovisionamiento');
    check(await ev(`!document.querySelector('.ap-deprov input').checked`), 'aprovisionamiento desmarcado por defecto');

    // 9. historial: «Recuperar» solo con A/B
    await emit('apps', { ...data, history: [
      { id: 'aaaaaaaaaaaa', display: 'El tiempo', at: '2026-10-29T10:00:00Z', op: 'remove-user', state: 'removed', restore: 'B', recovery: 'B' },
      { id: 'bbbbbbbbbbbb', display: 'Programa X', at: '2026-10-29T10:01:00Z', op: 'win32-settings', state: 'opened', restore: 'C', recovery: 'C' },
      { id: 'cccccccccccc', display: 'Correo y Calendario', at: '2026-10-29T10:02:00Z', op: 'remove-user', state: 'failed', error: 'Acceso denegado (0x80070005)', restore: 'D', recovery: 'D' }] });
    const hb = await ev(`[...document.querySelectorAll('#ap-hist-list li')].map(li => !!li.querySelector('button'))`);
    check(JSON.stringify(hb) === '[true,false,false]', 'historial: botón solo donde hay recuperación real', JSON.stringify(hb));

    // 10. cientos de apps: 600 simuladas
    const many = Array.from({ length: 600 }, (_, i) => fake({ name: `Prueba.App${i}`, display: `App de prueba ${i}`, category: i % 3 ? 'unknown' : 'optional', origin: 'third', publisher: 'Fabrikam' }));
    const t1 = Date.now(); await emit('apps', { ...data, apps: [...data.apps, ...many] }); const tMany = Date.now() - t1;
    check(await ev(`document.querySelectorAll('#ap-list li.ap-row').length`) === 200, '600+ apps: se dibujan 200 y el resto bajo «Mostrar todas»');
    check(tMany < 1500, '600+ apps se dibujan rápido', `${tMany} ms`);
    const t2 = Date.now(); await ev(`(() => { const q = document.getElementById('ap-q'); q.value = 'prueba 59'; q.dispatchEvent(new Event('input')); })()`); await sleep(250);
    check(await ev(`document.querySelectorAll('#ap-list li.ap-row').length`) === 16, 'búsqueda en 700 apps (59, 159…559 y 590–599)', await ev(`document.querySelectorAll('#ap-list li.ap-row').length`) + ` filas, ${Date.now() - t2} ms`);
    results.push(`info render inicial ${tRender} ms, 700 apps ${tMany} ms`);

    // 11. inglés: las etiquetas nuevas salen traducidas
    await cdp('Page.addScriptToEvaluateOnNewDocument', { source: `try { localStorage.setItem('vigia-lang', 'en'); } catch {}` });
    await cdp('Page.navigate', { url }); await sleep(1500);
    await ev(`document.querySelector('.nav[data-view="apps"]').click()`); await sleep(100);
    await emit('apps', data); await sleep(400);
    const en = await ev(`document.getElementById('view-apps').innerText`);
    fs.writeFileSync(path.join(outDir, 'view-apps-en.txt'), en);
    const esLeft = ['Detectadas', 'Protegidas', 'Revisar selección', 'Sistema protegido', 'Se pueden quitar', 'Recomendadas para revisar', 'En tu usuario', 'Seguro', 'Personalizado'].filter((w) => en.includes(w));
    check(esLeft.length === 0, 'inglés: sin restos en español en las etiquetas clave', esLeft.join(', '));
    check(en.includes('Detected') && en.includes('Review selection'), 'inglés: textos traducidos');
    await cdp('Page.captureScreenshot', { format: 'png' }).then((r) => fs.writeFileSync(path.join(outDir, 'apps-en.png'), Buffer.from(r.data, 'base64')));
  } catch (e) { check(false, 'excepción en la prueba', e.stack); }
  finally {
    const appErr = errors.filter((e) => !/favicon|ERR_FILE_NOT_FOUND/.test(e));
    check(appErr.length === 0, 'sin errores de JavaScript', appErr.slice(0, 5).join(' | '));
    console.log(results.join('\n'));
    console.log(`UI OK=${results.filter((r) => r.startsWith('OK')).length} FAIL=${fails}`);
    try { ws?.close(); } catch { /* */ }
    ch.kill();
    process.exitCode = fails ? 1 : 0;
  }
})();
