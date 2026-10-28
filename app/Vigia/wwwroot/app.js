(() => {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const host = window.chrome && window.chrome.webview;
  const send = (msg) => host && host.postMessage(msg);

  const fmt = (v, d = 0, unit = '') => (v === null || v === undefined || Number.isNaN(v)) ? 'N/D'
    : `${Number(v).toLocaleString(window.vigiaI18n.locale(), { minimumFractionDigits: d, maximumFractionDigits: d })}${unit}`;
  const gb = (b) => b >= 1073741824 ? `${(b / 1073741824).toFixed(2)} GB` : b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.round(b / 1024)} KB`;

  // ---------- navegación ----------
  document.querySelectorAll('.nav').forEach((b) => b.addEventListener('click', () => {
    document.querySelectorAll('.nav').forEach((n) => { n.classList.toggle('is-active', n === b); n.toggleAttribute('aria-current', n === b); });
    document.querySelectorAll('.view').forEach((v) => { v.hidden = v.id !== `view-${b.dataset.view}`; });
    if (b.dataset.view === 'monitor') resize();
  }));

  // ---------- telemetría ----------
  // Solo se avisa cuando los datos dejan de llegar; si todo va bien no hace falta decirlo.
  let lastTs = 0;
  setInterval(() => {
    const s = Math.round((Date.now() - lastTs) / 1000);
    if (lastTs && s >= 5) { $('clock').hidden = false; $('clock').textContent = `Sin lecturas desde hace ${s} s`; }
  }, 1000);
  const hist = [];      // {t, ct, gt, cl}
  const MAX = 300;
  let win = 60;

  function setTemp(el, v) {
    el.textContent = fmt(v);
    const p = el.parentElement;
    p.classList.toggle('na', v == null);
    p.classList.toggle('warm', v != null && v >= 70 && v < 85);
    p.classList.toggle('hot', v != null && v >= 85);
  }

  function render(s) {
    lastTs = Date.now(); window.vigiaLastTs = lastTs;
    $('clock').hidden = true;

    const c = s.cpu;
    $('cpu-name').textContent = c.name;
    setTemp($('cpu-temp'), c.temp);
    $('cpu-load').textContent = fmt(c.load, 0, ' %');
    $('cpu-clock').textContent = fmt(c.clockGhz, 2, ' GHz');
    $('cpu-cores').textContent = `${c.cores} / ${c.threads}`;

    const g = s.gpu;
    $('gpu-name').textContent = g ? g.name : 'No detectada';
    setTemp($('gpu-temp'), g ? g.temp : null);
    $('gpu-load').textContent = fmt(g && g.load, 0, ' %');
    $('gpu-clock').textContent = fmt(g && g.coreClockMhz, 0, ' MHz');
    $('gpu-vram').textContent = g && g.vramTotalGb ? `${fmt(g.vramUsedGb, 1)} / ${fmt(g.vramTotalGb, 1)} GB` : 'N/D';

    const r = s.ram;
    $('ram-kind').textContent = [r.type, r.speedMts ? `${r.speedMts} MT/s` : null].filter(Boolean).join(' · ') || 'Memoria del sistema';
    $('ram-used').textContent = fmt(r.usedGb, 1);
    $('ram-total').textContent = `/ ${fmt(r.totalGb, 0)} GB`;
    $('ram-bar').style.width = r.totalGb ? `${(r.usedGb / r.totalGb) * 100}%` : '0';
    $('ram-speed').textContent = r.speedMts ? `${r.speedMts} MT/s` : 'N/D';
    $('ram-lat').textContent = r.cl ? `CL${r.cl} · ${fmt(r.latencyNs, 1)} ns` : 'N/D';
    $('ram-temp').textContent = fmt(r.temp, 0, ' °C');

    const [pTitle, pText] = s.elevated
      ? ['Modo administrador', 'Sensores completos.']
      : ['Modo estándar', 'Ejecuta como administrador para ver temperaturas.'];
    if ($('privilege').dataset.k !== pTitle) { $('privilege').replaceChildren(el('strong', null, pTitle), pText); $('privilege').dataset.k = pTitle; }
    const notes = (s.notes || []).filter((n) => !n.startsWith('Sin privilegios'));
    $('notes').hidden = notes.length === 0;
    $('notes').textContent = notes.join(' ');

    hist.push({ t: s.ts, ct: c.temp, gt: g ? g.temp : null, cl: c.load });
    if (hist.length > MAX) hist.shift();
    draw();
    renderDrives(s.drives);
  }

  function renderDrives(drives) {
    const ul = $('drives');
    ul.replaceChildren(...drives.map((d) => {
      const pct = d.totalGb ? (d.usedGb / d.totalGb) * 100 : 0;
      const li = document.createElement('li');
      li.className = 'drive';
      li.innerHTML = `<span class="letter"></span><div><span class="name"></span><div class="bar"><i></i></div></div><div class="nums"><b></b><span></span></div>`;
      li.querySelector('.letter').textContent = d.letter;
      li.querySelector('.name').textContent = [d.label, d.model, d.temp != null ? `${fmt(d.temp)} °C` : null].filter(Boolean).join(' · ');
      const bar = li.querySelector('.bar i');
      bar.style.width = `${pct}%`;
      bar.classList.toggle('full', pct >= 90);
      li.querySelector('.nums b').textContent = `${fmt(d.freeGb, 1)} GB libres`;
      li.querySelector('.nums span').textContent = `${fmt(d.usedGb, 1)} de ${fmt(d.totalGb, 1)} GB · ${pct.toFixed(0)} %`;
      return li;
    }));
  }

  // ---------- gráfica ----------
  const cv = $('chart');
  const ctx = cv.getContext('2d');
  const css = (v) => getComputedStyle(document.documentElement).getPropertyValue(v).trim();

  function resize() {
    const r = cv.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    cv.width = Math.max(1, r.width * dpr);
    cv.height = Math.max(1, r.height * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    draw();
  }
  new ResizeObserver(resize).observe(cv);

  function draw() {
    const w = cv.clientWidth, h = cv.clientHeight;
    if (!w || !h) return;
    ctx.clearRect(0, 0, w, h);
    const padL = 34, padB = 20, padT = 6, pw = w - padL - 6, ph = h - padB - padT;
    const now = hist.length ? hist[hist.length - 1].t : Date.now();
    const from = now - win * 1000;
    const pts = hist.filter((p) => p.t >= from);
    const ys = pts.flatMap((p) => [p.ct, p.gt]).filter((v) => v != null);
    const top = Math.max(100, Math.ceil((Math.max(0, ...ys) + 5) / 10) * 10);

    ctx.font = '11px JBMono, monospace';
    ctx.fillStyle = css('--fg-3');
    ctx.strokeStyle = css('--line');
    ctx.lineWidth = 1;
    for (let v = 0; v <= top; v += top / 4) {
      const y = padT + ph - (v / top) * ph;
      ctx.globalAlpha = 0.6;
      ctx.beginPath(); ctx.moveTo(padL, Math.round(y) + 0.5); ctx.lineTo(w - 6, Math.round(y) + 0.5); ctx.stroke();
      ctx.globalAlpha = 1;
      ctx.fillText(String(Math.round(v)), 4, y + 4);
    }
    ctx.fillText(`-${win}s`, padL, h - 4);
    ctx.fillText(window.vigiaI18n.t('ahora'), w - 40, h - 4);

    const X = (t) => padL + ((t - from) / (win * 1000)) * pw;
    const Y = (v) => padT + ph - (Math.min(v, top) / top) * ph;
    const line = (key, color, fill) => {
      const seg = pts.filter((p) => p[key] != null);
      if (seg.length < 2) return;
      ctx.beginPath();
      seg.forEach((p, i) => (i ? ctx.lineTo(X(p.t), Y(p[key])) : ctx.moveTo(X(p.t), Y(p[key]))));
      ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.lineJoin = 'round'; ctx.stroke();
      if (fill) {
        ctx.lineTo(X(seg[seg.length - 1].t), padT + ph); ctx.lineTo(X(seg[0].t), padT + ph); ctx.closePath();
        const g = ctx.createLinearGradient(0, padT, 0, padT + ph);
        g.addColorStop(0, color.replace(')', ' / 0.22)')); g.addColorStop(1, color.replace(')', ' / 0)'));
        ctx.fillStyle = g; ctx.fill();
      }
    };
    line('cl', css('--violet'), false);
    line('gt', css('--cyan'), false);
    line('ct', css('--amber'), true);
    if (!pts.some((p) => p.ct != null)) {
      ctx.fillStyle = css('--fg-3'); ctx.font = '13px Plex, sans-serif';
      ctx.fillText(window.vigiaI18n.t('Temperatura de CPU no disponible en modo estándar.'), padL + 12, padT + 20);
    }
  }

  document.querySelectorAll('.seg button').forEach((b) => b.addEventListener('click', () => {
    document.querySelectorAll('.seg button').forEach((x) => x.setAttribute('aria-checked', String(x === b)));
    win = Number(b.dataset.win); draw();
  }));

  // ---------- limpieza ----------
  let targets = [];
  let lastRun = null;
  function renderTargets() {
    const ul = $('targets');
    if (!targets.length) return;
    ul.replaceChildren(...targets.map((t) => {
      const li = document.createElement('li');
      li.innerHTML = `<label><input type="checkbox"><span><span class="tname"></span><span class="path"></span></span><span class="size"></span></label>`;
      const cb = li.querySelector('input');
      cb.checked = t.bytes > 0; cb.disabled = t.bytes === 0; cb.dataset.id = t.id;
      cb.addEventListener('change', updateTotal);
      li.querySelector('.tname').textContent = t.name;
      li.querySelector('.path').textContent = `${t.path} · ${t.files} archivos`;
      li.querySelector('.size').textContent = gb(t.bytes);
      return li;
    }));
    updateTotal();
  }
  function selected() { return [...document.querySelectorAll('#targets input:checked')].map((c) => c.dataset.id); }
  function updateTotal() {
    const ids = selected();
    const sum = targets.filter((t) => ids.includes(t.id)).reduce((a, t) => a + t.bytes, 0);
    $('clean-total').textContent = ids.length ? `${gb(sum)} se pueden liberar` : 'Nada seleccionado';
    $('run').disabled = sum === 0;
  }
  $('scan').addEventListener('click', () => {
    $('scan').classList.add('busy'); $('scan').textContent = 'Analizando…';
    send({ cmd: 'cleanScan' });
  });
  $('run').addEventListener('click', () => {
    $('run').disabled = true; $('run').textContent = 'Limpiando…';
    send({ cmd: 'cleanRun', ids: selected() });
  });

  // ---------- utilidades DOM ----------
  const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };
  const stat = (label, value, extra) => { const d = el('div'); d.append(el('dt', null, label), el('dd', null, value)); if (extra) d.append(extra); return d; };
  const LV = { ok: 'Bien', info: 'Aviso', warn: 'Revisar', bad: 'Problema' };
  const nf = new Intl.NumberFormat(window.vigiaI18n.locale(), { useGrouping: 'always', maximumFractionDigits: 0 });
  const num = (v) => nf.format(Number(v));
  const plural = (n, one, many) => `${n} ${n === 1 ? one : many}`;
  const LVN = { ok: ['bien', 'bien'], info: ['aviso', 'avisos'], warn: ['para revisar', 'para revisar'], bad: ['problema', 'problemas'] };

  // ---------- salud de discos ----------
  const busyBtn = (b, on, txt) => { b.classList.toggle('busy', on); b.disabled = on; if (txt) b.textContent = txt; };
  $('h-scan').addEventListener('click', () => { busyBtn($('h-scan'), true, 'Escaneando…'); send({ cmd: 'healthScan' }); });
  function renderHealth(list) {
    busyBtn($('h-scan'), false, 'Escanear de nuevo');
    const box = $('health-list');
    if (!list.length) { box.replaceChildren(el('p', 'empty', 'No se encontraron discos.')); return; }
    box.replaceChildren(...list.map((d) => {
      const c = el('article', `hcard lv-${d.level}`);
      const head = el('header', 'hcard-head');
      const who = el('div', 'who');
      who.append(el('strong', null, d.model), el('span', null, [d.bus, d.media, `${num(d.sizeGb)} GB`].join(' · ')));
      head.append(el('span', 'letter', d.letters.join(' ') || '—'), who, el('span', 'pill', d.verdict));
      const st = el('dl', 'stats');
      const temp = d.temp != null ? `${fmt(d.temp)} °C` : 'N/D';
      const tempBox = stat('Temperatura', temp);
      if (d.warnTemp) tempBox.append(el('span', 'meta-sm', `alerta a ${fmt(d.warnTemp)} °C`));
      st.append(tempBox);
      if (d.lifePct != null) {
        const bar = el('div', 'life'); const i = el('i'); i.style.width = `${d.lifePct}%`;
        if (d.lifePct <= 10) i.style.background = 'var(--hot)'; bar.append(i);
        st.append(stat('Vida restante', `${fmt(d.lifePct)} %`, bar));
      } else st.append(stat('Vida restante', d.media === 'HDD' ? 'No aplica' : 'N/D'));
      st.append(stat('Horas encendido', d.powerOnHours != null ? `${num(d.powerOnHours)} h` : 'N/D'));
      st.append(stat('Encendidos', d.powerCycles != null ? num(d.powerCycles) : 'N/D'));
      st.append(stat('Escrito', d.writtenTb != null ? `${fmt(d.writtenTb, 1)} TB` : 'N/D'));
      st.append(stat('Reserva', d.availableSpare != null ? `${fmt(d.availableSpare)} % (mín. ${fmt(d.spareThreshold)})` : 'N/D'));
      c.append(head, st);
      if (d.attributes.length) {
        const ul = el('ul', 'attrs');
        d.attributes.forEach((a) => { const li = el('li', `lv-${a.level}`); li.title = a.hint || ''; li.append(el('span', null, a.name), el('b', null, num(a.raw))); ul.append(li); });
        c.append(ul);
      }
      const notes = [...d.issues, ...d.attributes.filter((a) => a.hint).map((a) => a.hint)];
      if (notes.length) { const ul = el('ul', 'issues'); notes.forEach((t) => ul.append(el('li', null, t))); c.append(ul); }
      return c;
    }));
  }

  // ---------- optimizar ----------
  let optBusy = false;
  $('o-analyze').addEventListener('click', () => { busyBtn($('o-analyze'), true, 'Analizando…'); send({ cmd: 'optStatus', analyze: true }); });
  $('op-cancel').addEventListener('click', () => { $('op-cancel').disabled = true; send({ cmd: 'optCancel' }); });
  function renderOpt(s) {
    busyBtn($('o-analyze'), false, 'Analizar fragmentación');
    const ul = $('vols');
    ul.replaceChildren(...s.volumes.map((v) => {
      const li = el('li', 'vol');
      const who = el('div');
      who.append(el('span', 'name', v.label), el('div', 'meta', `${v.media} · ${v.action}`));
      const last = el('dl', 'col'); last.append(stat('Última vez', v.lastRun ? `${v.lastRun}${v.lastOp ? ` · ${v.lastOp}` : ''}` : 'Sin registro'));
      const frag = el('dl', 'col frag');
      frag.append(stat('Fragmentación', v.media === 'HDD' ? (v.fragmented != null ? `${v.fragmented} %` : 'Sin analizar') : 'No aplica (SSD)'));
      const b = el('button', 'btn primary', 'Optimizar');
      b.disabled = optBusy;
      b.addEventListener('click', () => startOpt(v));
      li.append(el('span', 'letter', v.letter), who, last, frag, b);
      return li;
    }));
    $('sched').textContent = s.scheduled
      ? 'Windows ya optimiza estas unidades una vez por semana. Usa el botón si quieres hacerlo ahora.'
      : 'La optimización semanal automática de Windows está apagada.';
  }
  function startOpt(v) {
    optBusy = true;
    document.querySelectorAll('.vol .btn').forEach((b) => { b.disabled = true; });
    $('oprun').hidden = false; $('op-cancel').disabled = false;
    $('op-title').textContent = `${v.action} en ${v.letter} ${v.label}`;
    $('op-bar').style.width = '0'; $('op-bar').parentElement.classList.add('indet');
    $('op-line').textContent = 'Iniciando…'; $('op-result').textContent = '';
    send({ cmd: 'optRun', letter: v.letter });
  }

  // ---------- diagnóstico ----------
  $('d-run').addEventListener('click', () => { busyBtn($('d-run'), true, 'Revisando…'); send({ cmd: 'diagRun' }); send({ cmd: 'secGet' }); });

  // ---------- antivirus (Microsoft Defender) ----------
  // Vigía no trae antivirus propio: muestra si Defender protege y, si otra herramienta lo apagó, lo vuelve a encender con copia.
  let secBusy = false;
  function renderSec(s) {
    const box = $('sec'); box.hidden = false;
    const ok = s.running && s.realTime;
    box.className = `card sec ${ok ? 'lv-ok' : s.others.length ? 'lv-info' : 'lv-bad'}`;
    const body = [];
    const age = s.sigAgeDays == null ? '' : s.sigAgeDays === 0 ? ' Firmas de hoy.' : ` Firmas de hace ${plural(s.sigAgeDays, 'día', 'días')}.`;
    if (ok) body.push(el('p', null, `Microsoft Defender te está protegiendo.${age}`));
    else if (s.others.length) body.push(el('p', null, `Te protege ${s.others.join(', ')}. Defender se queda en pausa para no chocar.`));
    else if (s.needsReboot) body.push(el('p', null, 'Ya no hay nada que lo bloquee. Falta reiniciar el PC para que arranque.'));
    else if (s.running) body.push(el('p', null, 'Defender está encendido, pero la protección en tiempo real está apagada.'));
    else body.push(el('p', null, 'Microsoft Defender está apagado y no hay otro antivirus. Ahora mismo nada vigila lo que descargas o abres.'));
    if (!ok && s.blocks.length && !s.others.length) {
      const ul = el('ul', 'sec-why'); s.blocks.forEach((b) => ul.append(el('li', null, b.label))); body.push(el('p', 'sec-h', 'Lo que lo tiene apagado:'), ul);
    }
    const acts = el('div', 'sec-acts');
    if (!ok && !s.others.length && s.blocks.length) {
      const b = el('button', 'btn', 'Activar Defender');
      bkArm(b, 'Activar Defender', '¿Seguro? Activar', () => { secBusy = true; busyBtn(b, true, 'Activando…'); send({ cmd: 'secEnable' }); });
      body.push(el('p', 'src', 'Guarda una copia, quita las reglas que lo apagan y deja sus servicios como vienen de fábrica. Después hay que reiniciar.'));
      acts.append(b);
    }
    if (s.canScan) {
      const b = el('button', 'btn', 'Actualizar y analizar');
      b.addEventListener('click', () => { secBusy = true; busyBtn(b, true, 'Analizando…'); send({ cmd: 'secScan' }); });
      acts.append(b);
    }
    const msg = el('p', 'sec-msg'); msg.id = 'sec-msg';
    const keep = $('sec-msg'); if (keep && (secBusy || keep.dataset.keep)) { msg.textContent = keep.textContent; msg.dataset.keep = keep.dataset.keep || ''; }
    box.replaceChildren(el('strong', null, 'Antivirus'), ...body, acts, msg);
  }

  function renderDiag(r) {
    busyBtn($('d-run'), false, 'Volver a revisar');
    const s = r.system;
    const up = s.uptimeHours >= 48 ? `${Math.round(s.uptimeHours / 24)} días` : `${fmt(s.uptimeHours, 0)} h`;
    $('sysinfo').replaceChildren(stat('Sistema', `${s.os} ${s.build}`), stat('Placa base', s.board || 'N/D'), stat('BIOS', s.bios || 'N/D'), stat('Encendido', up));
    [...$('sysinfo').querySelectorAll('dd')].forEach((d) => { d.title = d.textContent; });
    const count = (l) => r.items.filter((i) => i.level === l).length;
    $('tally').replaceChildren(...['bad', 'warn', 'info', 'ok'].filter((l) => count(l)).map((l) => el('span', `pill lv-${l}`, `${count(l)} ${LVN[l][count(l) === 1 ? 0 : 1]}`)));
    $('diag-sub').textContent = `Registro de eventos de Windows, últimos ${r.days} días. Solo lectura. Revisado a las ${new Date(r.ts).toLocaleTimeString(window.vigiaI18n.locale())}.`;
    const order = { bad: 0, warn: 1, info: 2, ok: 3 };
    $('findings').replaceChildren(...[...r.items].sort((a, b) => order[a.level] - order[b.level]).map((i) => {
      const li = el('li', `finding lv-${i.level}`);
      const body = el('div'); body.append(el('p', null, i.detail));
      if (i.examples.length) {
        const det = el('details'); det.append(el('summary', null, `Ver ${plural(i.examples.length, 'detalle', 'detalles')}`));
        const ul = el('ul'); i.examples.forEach((x) => ul.append(el('li', null, x))); det.append(ul); body.append(det);
      }
      li.append(el('span', 'dot'), el('strong', null, i.title), body);
      return li;
    }));
  }


  // ---------- sugerencias ----------
  // Se revisa una vez al abrir la ventana (unos 2 s de pings y lecturas) y cuando se pide; nunca con la ventana oculta.
  const GO = { health: 'Abrir Salud de discos', storage: 'Abrir Almacenamiento', mem: 'Abrir Memoria' };
  let sugDone = false;
  $('s-run').addEventListener('click', () => { busyBtn($('s-run'), true, 'Revisando…'); send({ cmd: 'suggest' }); });
  function renderSug(r) {
    sugDone = true;
    busyBtn($('s-run'), false, 'Volver a revisar');
    const n = r.items.filter((i) => i.level === 'warn' || i.level === 'bad').length;
    $('sug-count').hidden = !n; $('sug-count').textContent = n;
    $('sug-sub').textContent = `Lo que se puede mejorar en tu PC y por qué. Vigía no cambia nada de esto por su cuenta. Revisado a las ${new Date(r.ts).toLocaleTimeString(window.vigiaI18n.locale())}.`;
    if (!r.items.length) { $('sugs').replaceChildren(el('li', 'empty', 'Nada que sugerir ahora mismo.')); return; }
    $('sugs').replaceChildren(...r.items.map((i) => {
      const li = el('li', `finding lv-${i.level}`);
      const body = el('div');
      // una frase por nodo: así cada una se traduce sola
      const sentences = (p, list) => { list.forEach((x, k) => { if (k) p.append(' '); p.append(el('span', null, x)); }); return p; };
      body.append(sentences(el('p'), i.why));
      const todo = el('p', 'sug-do'); todo.append(el('b', null, 'Qué hacer'), ' '); body.append(sentences(todo, i.do));
      const foot = el('div', 'sug-foot');
      if (i.go && GO[i.go]) {
        const b = el('button', 'link', GO[i.go]);
        b.addEventListener('click', () => document.querySelector(`.nav[data-view="${i.go}"]`).click());
        foot.append(b);
      }
      if (i.facts.length) {
        const det = el('details'); det.append(el('summary', null, 'Lo medido'));
        const ul = el('ul'); i.facts.forEach((x) => ul.append(el('li', null, x))); det.append(ul); foot.append(det);
      }
      if (foot.childNodes.length) body.append(foot);
      li.append(el('span', 'dot'), el('strong', null, i.title), body);
      return li;
    }));
  }
  function sugAuto() { if (!sugDone && document.visibilityState === 'visible') send({ cmd: 'suggest' }); }
  setTimeout(sugAuto, 20000);
  document.addEventListener('visibilitychange', () => setTimeout(sugAuto, 3000));

  // ---------- memoria ----------
  const GBf = (v) => `${(v ?? 0).toLocaleString(window.vigiaI18n.locale(), { minimumFractionDigits: 1, maximumFractionDigits: 1 })} GB`;
  function renderMem(m) {
    const t = m.totalGb || 1;
    const procs = Math.max(0, m.usedGb - m.modifiedGb);
    const w = (v) => `${Math.max(0, (v / t) * 100)}%`;
    const map = document.querySelector('.memmap');
    map.querySelector('.m-used').style.width = w(procs);
    map.querySelector('.m-mod').style.width = w(m.modifiedGb);
    map.querySelector('.m-stby').style.width = w(m.standbyGb);
    const sw = (c) => { const i = el('i'); i.style.background = c; return i; };
    const dt = (c, label) => { const d = el('dt'); if (c) d.append(sw(c)); d.append(label); return d; };
    const item = (c, label, value) => { const d = el('div'); d.append(dt(c, label), el('dd', null, value)); return d; };
    $('memstats').replaceChildren(
      item('var(--violet)', 'En uso', `${GBf(m.usedGb)} de ${GBf(m.totalGb)} · ${m.loadPct} %`),
      item('oklch(0.48 0.04 245)', 'Caché en espera', `${GBf(m.standbyGb)} · ${GBf(m.standbyLowGb)} de baja prioridad`),
      item('var(--amber)', 'Modificada', GBf(m.modifiedGb)),
      item(null, 'Libre sin caché', GBf(m.freeGb)),
    );
  }
  function memOpts() {
    const o = {};
    document.querySelectorAll('#view-mem [data-opt]').forEach((c) => { o[c.dataset.opt] = c.checked; });
    return o;
  }
  $('m-free').addEventListener('click', () => {
    busyBtn($('m-free'), true, 'Liberando…');
    send({ cmd: 'memFree', opts: memOpts() });
  });
  let cfg = null;
  function saveCfg() {
    if (!cfg) return;
    cfg.memAuto = $('m-auto').checked;
    cfg.memAutoThreshold = Number($('m-thr').value) || 85;
    cfg.memAutoIntervalMin = Number($('m-int').value) || 5;
    send({ cmd: 'settingsSet', cfg });
  }
  ['m-auto', 'm-thr', 'm-int'].forEach((id) => $(id).addEventListener('change', saveCfg));
  function renderFreed(r) {
    const mb = r.freedMb;
    const amount = mb >= 1024 ? GBf(mb / 1024) : `${num(Math.round(mb))} MB`;
    const failed = r.steps.filter((s) => !s.ok);
    let txt = `${r.auto ? 'Liberación automática: ' : ''}${mb < 1 ? 'No había nada que liberar' : `Liberados ${amount}`}. Uso ${r.before.loadPct} % → ${r.after.loadPct} %.`;
    if (failed.length) txt += ` No se pudo: ${failed.map((s) => `${s.name.toLowerCase()} (${s.error})`).join('; ')}.`;
    const skipped = r.steps.filter((s) => s.ok && s.error);
    if (skipped.length) txt += ` ${skipped.map((s) => `${s.name}: ${s.error}`).join('; ')}.`;
    $('m-result').textContent = txt;
    $('m-result').classList.toggle('bad', failed.length === r.steps.length);
    busyBtn($('m-free'), false, 'Liberar ahora');
  }

  // ---------- ajustes ----------
  const STATE = { applied: 'Aplicado', partial: 'A medias', off: 'Sin aplicar', na: 'No aplica aquí' };
  let tweaks = [];
  const twSel = () => [...document.querySelectorAll('#tw-groups input:checked')].map((c) => c.dataset.id);
  function twButtons() {
    const ids = new Set(twSel());
    const sel = tweaks.filter((t) => ids.has(t.id));
    $('tw-apply').disabled = !sel.some((t) => !t.blocked && t.state !== 'applied' && t.state !== 'na');
    $('tw-revert').disabled = !sel.some((t) => t.hasBackup);
    $('tw-apply').textContent = sel.length ? `Aplicar (${sel.length})` : 'Aplicar';
  }
  function renderTweaks(p) {
    tweaks = p.items;
    const keep = new Set(twSel());
    const groups = [...new Set(tweaks.map((t) => t.group))];
    $('tw-groups').replaceChildren(...groups.map((g) => {
      const sec = el('section', 'tw-group');
      sec.append(el('h2', null, g));
      const ul = el('ul', 'rows');
      tweaks.filter((t) => t.group === g).forEach((t) => {
        const li = el('li', `row${t.blocked ? ' off-limits' : ''}`);
        const cb = el('input'); cb.type = 'checkbox'; cb.dataset.id = t.id; cb.checked = keep.has(t.id);
        cb.disabled = !!t.blocked && !t.hasBackup;
        cb.addEventListener('change', twButtons);
        const id = `tw-${t.id}`; cb.id = id;
        const body = el('label'); body.htmlFor = id;
        body.append(el('strong', null, t.title), el('p', null, t.detail));
        const src = el('p', 'src');
        src.append(t.source);
        if (t.risk !== 'bajo') { src.append(' · '); src.append(el('span', `risk-${t.risk}`, `riesgo ${t.risk}`)); }
        if (t.reboot) src.append(' · pide reiniciar');
        body.append(src);
        if (t.blocked) body.append(el('p', 'src why', t.blocked));
        li.append(cb, body, el('span', `state ${t.state}`, STATE[t.state] || t.state));
        ul.append(li);
      });
      sec.append(ul);
      return sec;
    }));
    const applied = tweaks.filter((t) => t.state === 'applied').length;
    const rp = p.restorePoints < 0 ? 'no se pudo leer los puntos de restauración' : plural(p.restorePoints, 'punto de restauración', 'puntos de restauración');
    $('tw-sub').textContent = `${applied} de ${tweaks.length} ya están aplicados en este PC · ${rp}. Antes de aplicar se crea uno nuevo; Vigía guarda además el valor que tenías en cada cambio.`;
    twButtons();
  }
  $('tw-apply').addEventListener('click', () => { busyBtn($('tw-apply'), true, 'Aplicando…'); $('tw-revert').disabled = true; send({ cmd: 'tweaksApply', ids: twSel() }); });
  $('tw-revert').addEventListener('click', () => { busyBtn($('tw-revert'), true, 'Deshaciendo…'); $('tw-apply').disabled = true; send({ cmd: 'tweaksRevert', ids: twSel() }); });
  $('tw-utilidad-externa').addEventListener('click', () => { busyBtn($('tw-utilidad-externa'), true, 'Creando punto de restauración…'); send({ cmd: 'utilidad-externa' }); });
  function renderTweaksDone(b) {
    busyBtn($('tw-apply'), false, 'Aplicar'); busyBtn($('tw-revert'), false, 'Deshacer');
    const ok = b.results.filter((r) => r.ok).length, bad = b.results.filter((r) => !r.ok);
    let txt = `${plural(ok, 'cambio hecho', 'cambios hechos')}`;
    if (bad.length) txt += `; ${bad.length} con problemas: ${bad.map((r) => `${(tweaks.find((t) => t.id === r.id) || {}).title || r.id} (${r.error})`).join('; ')}`;
    if (b.restorePoint) txt += `. ${b.restorePoint}`;
    if (b.needsReboot) txt += ' Algunos cambios se notan después de reiniciar.';
    $('tw-result').textContent = txt.endsWith('.') ? txt : `${txt}.`;
    $('tw-result').classList.toggle('bad', ok === 0 && bad.length > 0);
    document.querySelectorAll('#tw-groups input:checked').forEach((c) => { c.checked = false; });
  }

  // ---------- apps de Windows ----------
  let bloat = [];
  const apSel = () => [...document.querySelectorAll('#ap-list input:checked')].map((c) => c.dataset.id);
  function renderBloat(list) {
    bloat = list;
    isoApps();
    if (!$('view-apps').hidden) send({ cmd: 'tweaksList' });
    const shown = list.filter((a) => a.installed);
    $('ap-remove').disabled = true;
    $('ap-rec').hidden = $('ap-remove').hidden = !shown.length;
    if (!shown.length) {
      $('ap-list').replaceChildren(el('li', 'empty', `No queda instalada ninguna de las ${list.length} apps de la lista. No hay nada que quitar.`));
      return;
    }
    $('ap-list').replaceChildren(...shown.map((a) => {
      const li = el('li', `row${a.blocked ? ' off-limits' : ''}`);
      const cb = el('input'); cb.type = 'checkbox'; cb.dataset.id = a.id; cb.id = `ap-${a.id}`; cb.disabled = !!a.blocked;
      cb.addEventListener('change', () => { const n = apSel().length; $('ap-remove').disabled = !n; $('ap-remove').textContent = n ? `Quitar (${n})` : 'Quitar seleccionadas'; });
      const body = el('label'); body.htmlFor = cb.id;
      body.append(el('strong', null, a.name), el('p', null, a.detail));
      if (a.blocked) body.append(el('p', 'src why', a.blocked));
      li.append(cb, body, el('span', 'state', a.version ? `v${a.version}` : ''));
      return li;
    }));
  }
  $('ap-remove').addEventListener('click', () => { busyBtn($('ap-remove'), true, 'Quitando…'); send({ cmd: 'bloatRemove', ids: apSel() }); });
  $('ap-rec').addEventListener('click', () => {
    document.querySelectorAll('#ap-list input[type=checkbox]').forEach((c) => { const a = bloat.find((b) => b.id === c.dataset.id); if (a && !c.disabled) c.checked = a.risk === 'bajo'; });
    const n = apSel().length; $('ap-remove').disabled = !n; $('ap-remove').textContent = n ? `Quitar (${n})` : 'Quitar seleccionadas';
  });
  // Sin «Sin anuncios ni apps sugeridas», Windows vuelve a instalar parte de estas apps por su cuenta.
  function apNote(items) {
    const t = items.find((x) => x.id === 'suggestions-off'); const n = $('ap-note');
    if (!t || t.state === 'applied') { n.hidden = true; return; }
    const b = el('button', 'btn', 'Evitarlo');
    b.addEventListener('click', () => { busyBtn(b, true, 'Aplicando…'); send({ cmd: 'tweaksApply', ids: ['suggestions-off'] }); });
    const row = el('div', 'ap-note-act'); row.append(b, el('span', 'sub', '(activa «Sin anuncios ni apps sugeridas», con copia).'));
    n.hidden = false; n.replaceChildren(el('p', null, 'Windows puede volver a instalar algunas por su cuenta.'), row);
  }

  // ---------- BIOS ----------
  function renderBios(b) {
    busyBtn($('bi-reload'), false, 'Volver a leer');
    const f = b.fw, box = $('bi-fw');
    const kv = (k, v) => { const d = el('div', 'bi-kv'); d.append(el('span', null, k), el('b', null, v)); return d; };
    const grid = el('div', 'bi-grid');
    grid.append(kv('Placa', f.board || 'N/D'), kv('BIOS', `${f.version || 'N/D'}${f.date ? ` · ${new Date(f.date).toLocaleDateString(window.vigiaI18n.locale(), { year: 'numeric', month: 'short', day: 'numeric' })}` : ''}`),
      kv('Arranque', f.uefi ? 'UEFI' : 'Legacy (CSM)'));
    const acts = el('div', 'ds-act');
    if (f.supportUrl) { const s1 = el('button', 'btn', 'Descargas de tu placa'); s1.addEventListener('click', () => send({ cmd: 'biosSupport' })); acts.append(s1); }
    if (f.uefi) {
      const r = el('button', 'btn', 'Reiniciar y entrar a la BIOS');
      bkArm(r, 'Reiniciar y entrar a la BIOS', '¿Seguro? Guarda lo que tengas abierto', () => { busyBtn(r, true, 'Reiniciando…'); send({ cmd: 'biosReboot' }); });
      acts.append(r);
    }
    box.replaceChildren(grid, acts);
    $('bi-list').replaceChildren(...b.checks.map((c) => {
      const li = el('li', `finding lv-${c.level}`);
      const body = el('div'); body.append(el('p', null, c.detail));
      if (c.how) body.append(el('p', 'src', c.how));
      li.append(el('span', 'dot'), el('strong', null, c.title), body);
      return li;
    }));
  }
  $('bi-reload').addEventListener('click', () => { busyBtn($('bi-reload'), true, 'Leyendo…'); send({ cmd: 'biosGet' }); });

  // ---------- ISO de Windows ----------
  let isoSrc = null, isoOut = null, isoBusy = false;
  function isoApps() {
    const list = bloat.length ? bloat : [];
    const box = $('iso-apps');
    if (box.childElementCount || !list.length) return;
    box.replaceChildren(...list.map((a) => {
      const l = el('label', 'chk'); const c = el('input'); c.type = 'checkbox'; c.dataset.id = a.id; c.checked = a.risk === 'bajo';
      c.addEventListener('change', isoSum);
      const t = el('span'); t.append(el('b', null, a.name), el('span', null, a.detail)); l.append(c, t); return l;
    }));
    isoSum();
  }
  function isoSum() { const n = document.querySelectorAll('#iso-apps input:checked').length; $('iso-apps-sum').textContent = `Apps que se quitan (${n})`; }
  function isoReady() {
    const local = $('iso-local').checked, u = $('iso-user').value.trim();
    $('iso-user').hidden = !local;
    $('iso-build').disabled = isoBusy || !isoSrc || !isoOut || (local && !/^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,19}$/u.test(u));
  }
  ['iso-local', 'iso-user'].forEach((id) => $(id).addEventListener('input', isoReady));
  $('iso-pick').addEventListener('click', () => send({ cmd: 'isoPick' }));
  $('iso-ms').addEventListener('click', () => send({ cmd: 'isoGetWindows' }));
  $('iso-saveas').addEventListener('click', () => isoSrc && send({ cmd: 'isoSaveAs', source: isoSrc.path }));
  $('iso-cancel').addEventListener('click', () => { busyBtn($('iso-cancel'), true, 'Cancelando…'); send({ cmd: 'isoCancel' }); });
  function renderIsoSource(s) {
    if (s.busy) { $('iso-src').textContent = `Leyendo ${s.path}…`; $('iso-src').classList.remove('bad'); return; }
    if (s.error) { $('iso-src').textContent = `${s.path}: ${s.error}`; $('iso-src').classList.add('bad'); isoSrc = null; $('iso-opts').hidden = $('iso-go').hidden = true; return; }
    $('iso-src').classList.remove('bad');
    isoSrc = s; $('iso-src').textContent = `${s.path} · ${gb(s.bytes)}`;
    const sel = $('iso-ed');
    sel.replaceChildren(...s.editions.map((e) => { const o = el('option', null, e.name); o.value = e.index; return o; }));
    const pro = s.editions.find((e) => /\bPro\b/i.test(e.name) && !/Education|Workstation|N\b/i.test(e.name)); if (pro) sel.value = pro.index;
    sel.disabled = s.editions.length < 2;
    isoOut = s.path.replace(/\.iso$/i, '') + '-vigia.iso';
    $('iso-out').textContent = `Se guarda como ${isoOut}`;
    $('iso-opts').hidden = $('iso-go').hidden = false;
    if (!bloat.length) send({ cmd: 'bloatList' }); else isoApps();
    isoReady();
  }
  $('iso-build').addEventListener('click', () => {
    isoBusy = true; isoReady();
    $('iso-result').textContent = ''; $('iso-result').classList.remove('bad');
    $('iso-run').hidden = false; $('iso-step').textContent = 'Empezando…'; $('iso-line').textContent = ''; $('iso-bar').style.width = '0';
    busyBtn($('iso-cancel'), false, 'Cancelar');
    send({ cmd: 'isoBuild', options: {
      source: isoSrc.path, output: isoOut, index: +$('iso-ed').value,
      apps: [...document.querySelectorAll('#iso-apps input:checked')].map((c) => c.dataset.id),
      noAds: $('iso-ads').checked, localAccount: $('iso-local').checked, user: $('iso-user').value.trim() || null, skipChecks: $('iso-skip').checked,
    } });
  });
  function isoProgress(p) {
    $('iso-step').textContent = `Paso ${p.num} de ${p.of}: ${p.step}`;
    const bar = $('iso-bar').parentElement;
    if (p.pct == null) { bar.classList.add('indet'); $('iso-line').textContent = ''; }
    else { bar.classList.remove('indet'); $('iso-bar').style.width = `${p.pct}%`; $('iso-line').textContent = `${Math.floor(p.pct)} %`; }
  }
  function isoDone(d) {
    isoBusy = false; $('iso-run').hidden = true; isoReady();
    const r = $('iso-result'); r.classList.toggle('bad', !d.ok);
    if (!d.ok) { r.textContent = d.message; return; }
    r.replaceChildren(`${d.message} ${gb(d.bytes)} en ${Math.max(1, Math.round(d.minutes))} min. `);
    const b = el('button', 'btn sp-inline', 'Mostrar en la carpeta'); b.addEventListener('click', () => send({ cmd: 'isoShow', path: d.output })); r.append(b);
  }

  // ---------- herramientas ----------
  function renderTools(p) {
    const mine = p.items.filter((t) => t.kind === 'folder');
    const apps = p.items.filter((t) => t.kind === 'app');
    $('tl-sub').textContent = `${mine.filter((t) => t.found).length} programas de ${p.root}, ${apps.length === 1 ? '1 programa instalado' : `${apps.length} programas instalados`} y ${p.items.length - mine.length - apps.length} herramientas de Windows. Vigía solo los abre; no los copia ni los cambia.`;
    const groups = [...new Set(p.items.map((t) => t.group))];
    $('tl-groups').replaceChildren(...groups.map((g) => {
      const sec = el('section', 'tw-group'); sec.append(el('h2', null, g));
      const grid = el('div', 'tools');
      p.items.filter((t) => t.group === g).forEach((t) => {
        const card = el('div', 'tool');
        const install = t.kind === 'app' && !t.found;
        const b = el('button', 'btn', install ? 'Instalar' : t.kind === 'cmd' || t.kind === 'action' ? 'Ejecutar' : 'Abrir'); b.disabled = !t.found && !install;
        b.dataset.tool = t.id;
        b.addEventListener('click', () => {
          if (t.id === 'restorepoint') busyBtn(b, true, 'Creando…');
          send({ cmd: 'toolOpen', id: t.id });
        });
        card.append(el('strong', null, t.name), b, el('p', null, t.found || install ? t.detail + (install ? ' No está instalado: se instala desde winget.' : '') : 'No está en la carpeta.'));
        if (t.warning) card.append(el('p', 'warn', t.warning));
        grid.append(card);
      });
      sec.append(grid);
      return sec;
    }));
  }

  // ---------- inicio de Windows ----------
  const suOrder = ['Programas', 'Tareas programadas', 'Servicios'];
  const suNote = {
    'Programas': 'Apagarlos es lo mismo que en el Administrador de tareas.',
    'Tareas programadas': 'Tareas que se lanzan al encender o al iniciar sesión. Se deshabilitan, no se borran.',
    'Servicios': 'Servicios de otros programas. Apagar = pasan a manual: arrancan cuando el programa los pida.',
  };
  function renderStartup(list) {
    busyBtn($('su-reload'), false, 'Volver a leer');
    const on = list.filter((s) => s.enabled).length;
    $('su-sub').textContent = `${plural(list.length, 'cosa arranca', 'cosas arrancan')} con Windows; ${on} ${on === 1 ? 'encendida' : 'encendidas'}. Apagar no borra nada: la entrada se queda y la puedes volver a encender.`;
    $('su-groups').replaceChildren(...suOrder.filter((g) => list.some((s) => s.group === g)).map((g) => {
      const sec = el('section', 'tw-group');
      sec.append(el('h2', null, g), el('p', 'sub su-note', suNote[g]));
      const ul = el('ul', 'rows');
      list.filter((s) => s.group === g).forEach((s) => {
        const li = el('li', `row su${s.enabled ? '' : ' is-off'}`);
        const body = el('div');
        const title = el('strong', null, s.name);
        body.append(title, el('p', null, [s.publisher, s.where].filter(Boolean).join(' · ')), Object.assign(el('p', 'src cmd', s.command), { title: s.command }));
        if (s.missing) body.append(el('p', 'src why', 'El programa ya no está en el disco: es seguro apagarlo.'));
        if (s.warning) body.append(el('p', 'src why', s.warning));
        const b = el('button', `btn${s.enabled ? '' : ' primary'}`, s.enabled ? 'Apagar' : 'Encender');
        b.addEventListener('click', () => { busyBtn(b, true, s.enabled ? 'Apagando…' : 'Encendiendo…'); send({ cmd: 'startupSet', id: s.id, on: !s.enabled }); });
        li.append(el('span', `state ${s.enabled ? 'applied' : ''}`, s.enabled ? 'Arranca' : 'Apagado'), body, b);
        ul.append(li);
      });
      sec.append(ul);
      return sec;
    }));
  }
  $('su-reload').addEventListener('click', () => { busyBtn($('su-reload'), true, 'Leyendo…'); send({ cmd: 'startupList' }); });


  // ---------- juegos: FPS antes y después + perfil de Fortnite ----------
  const fpsFmt = (n) => (Math.round(n * 10) / 10).toLocaleString(window.vigiaI18n.locale(), { maximumFractionDigits: n >= 100 ? 0 : 1 });
  const gmBase = (game) => localStorage.getItem('vigia.fpsBase.' + game);
  function gmSetup(s) {
    const out = [];
    if (s.modo) out.push(`modo ${s.modo}`);
    if (s.perfil) out.push(s.perfil === 'sí' ? 'perfil de Vigía' : 'sin perfil de Vigía');
    if (s.ajustes != null) out.push(`${s.ajustes} ajustes de Vigía`);
    if (s.pantalla) out.push(s.pantalla);
    return out.join(' · ');
  }
  function renderFpsState(st) {
    const m = $('gm-measure'), c = $('gm-cancel'), p = $('gm-state');
    m.hidden = st.active; c.hidden = !st.active; $('gm-dur').disabled = st.active;
    if (st.phase === 'waiting') p.textContent = 'Esperando tu juego. Ábrelo y juega normal; Vigía empieza sola cuando lo tengas delante.';
    else if (st.phase === 'found') p.textContent = `${st.game} detectado. Empiezo a medir en unos segundos, cuando termine de cargar.`;
    else if (st.phase === 'measuring') p.textContent = `Midiendo ${st.game}: ${Math.floor(st.done / 60)}:${String(st.done % 60).padStart(2, '0')} de ${Math.round(st.target / 60)} min. Si sales del juego, se pausa.`;
    else if (st.phase === 'done') p.textContent = `Medición de ${st.game} guardada.`;
    else if (st.phase === 'error') p.textContent = st.error || 'No se pudo medir.';
    else p.textContent = '';
  }
  function renderFps(fps) {
    renderFpsState(fps.state);
    const hist = [...fps.history].reverse();
    // comparación por juego: la medición marcada como «antes» (o la primera) contra la última
    const games = [...new Set(hist.map((r) => r.game))];
    $('gm-compare').replaceChildren(...games.map((g) => {
      const runs = fps.history.filter((r) => r.game === g);
      const last = runs[runs.length - 1];
      const base = runs.find((r) => r.id === gmBase(g)) || runs[0];
      const box = el('div', 'gm-cmp');
      box.append(el('strong', null, g));
      if (base === last) {
        box.append(el('p', null, `${fpsFmt(last.avg)} FPS de media, ${fpsFmt(last.low1)} en el 1 % más lento y ${fpsFmt(last.max)} de máximo.`),
          el('p', 'src', 'Optimiza y mide otra partida igual para ver la diferencia.'));
        return box;
      }
      // la diferencia sale de los números que se ven, para que la resta cuadre a ojo
      const shown = (n) => (n >= 100 ? Math.round(n) : Math.round(n * 10) / 10);
      const d = shown(last.avg) - shown(base.avg), pct = base.avg ? (d / shown(base.avg)) * 100 : 0;
      const dl = shown(last.low1) - shown(base.low1);
      const big = el('p', `gm-delta ${d >= 0 ? 'up' : 'down'}`, `${d >= 0 ? '+' : '−'}${fpsFmt(Math.abs(d))} FPS (${d >= 0 ? '+' : '−'}${fpsFmt(Math.abs(pct))} %)`);
      const grid = el('div', 'gm-grid');
      [['', 'Media', '1 % bajos', 'Máximo'], ['Antes', fpsFmt(base.avg), fpsFmt(base.low1), fpsFmt(base.max)], ['Ahora', fpsFmt(last.avg), fpsFmt(last.low1), fpsFmt(last.max)]]
        .forEach((row, i) => row.forEach((c) => grid.append(el('span', i === 0 ? 'gm-h' : null, c))));
      box.append(big, grid);
      box.append(el('p', 'src', `1 % bajos: ${dl >= 0 ? '+' : '−'}${fpsFmt(Math.abs(dl))} FPS. Es lo que notas como tirones; importa tanto como la media.`));
      const bm = base.setup?.modo, lm = last.setup?.modo;
      if (bm && lm && bm !== lm) box.append(el('p', 'src', `Cambió el modo del juego (${bm} → ${lm}): parte de la diferencia viene de ahí, no solo de optimizar Windows.`));
      if (Math.abs(pct) < 3) box.append(el('p', 'src', 'Menos de un 3 % de diferencia entra en lo normal entre una partida y otra: para estar seguro, mide otra vez.'));
      return box;
    }));
    $('gm-hist').replaceChildren(...(hist.length ? hist.map((r) => {
      const li = el('li', 'row gm-run');
      const body = el('div');
      const d = new Date(r.at);
      body.append(el('strong', null, `${r.game} · ${fpsFmt(r.avg)} FPS`),
        el('p', null, `1 % bajos ${fpsFmt(r.low1)} · máximo ${fpsFmt(r.max)} · ${Math.round(r.seconds / 60 * 10) / 10} min medidos`),
        el('p', 'src', `${d.toLocaleDateString(window.vigiaI18n.locale(), { day: 'numeric', month: 'short' })}, ${d.toLocaleTimeString(window.vigiaI18n.locale(), { hour: '2-digit', minute: '2-digit' })} · ${gmSetup(r.setup || {})}`));
      const acts = el('div', 'gm-acts');
      const isBase = (gmBase(r.game) || fps.history.find((x) => x.game === r.game)?.id) === r.id;
      const mark = el('button', 'btn', isBase ? 'Es el «antes»' : 'Usar como «antes»');
      mark.disabled = isBase;
      mark.addEventListener('click', () => { localStorage.setItem('vigia.fpsBase.' + r.game, r.id); renderFps(fps); });
      const del = el('button', 'btn', 'Borrar');
      del.addEventListener('click', () => { del.disabled = true; send({ cmd: 'fpsDelete', id: r.id }); });
      acts.append(mark, del);
      li.append(el('span', `state ${isBase ? '' : 'applied'}`, isBase ? 'Antes' : 'Después'), body, acts);
      return li;
    }) : [el('li', 'empty', 'Todavía no hay mediciones. Mide una partida ahora, antes de optimizar: esa será tu referencia.')]));
  }
  const fnLevel = ['Bajo', 'Medio', 'Alto', 'Épico', 'Cinemático'];
  function fnValue(s) {
    const v = s.value;
    if (/^(true|false)$/i.test(v)) return /^true$/i.test(v) ? 'Sí' : 'No';
    if (s.key.startsWith('sg.') && s.key !== 'sg.ResolutionQuality') return fnLevel[+v] ?? v;
    if (s.key === 'sg.ResolutionQuality') return `${Math.round(+v)} %`;
    if (/FrameRateLimit$/.test(s.key)) return +v === 0 ? 'Sin límite' : `${Math.round(+v)} FPS`;
    return v;
  }
  // «Tu PC para jugar»: pantallas, RAM, Resizable BAR, modo juego, HAGS y energía
  let rigKeepT = null;
  function renderRig(list) {
    const ul = $('gm-rig'); if (!ul || !list) return;
    const warn = list.filter((c) => c.level === 'warn').length;
    $('gm-rig-sum').textContent = warn ? `${plural(warn, 'cosa por aprovechar', 'cosas por aprovechar')}` : 'Todo aprovechado';
    $('gm-rig-sum').className = `pill ${warn ? 'lv-warn' : 'lv-ok'}`;
    ul.replaceChildren(...list.map((c) => {
      const li = el('li', `finding lv-${c.level}`);
      const body = el('div'); body.append(el('p', null, c.detail));
      if (c.how) body.append(el('p', 'src', c.how));
      if (c.fix) {
        const btn = el('button', 'btn primary', c.fixLabel);
        btn.addEventListener('click', () => {
          busyBtn(btn, true, 'Cambiando…');
          const [kind, ...rest] = c.fix.split(':');
          if (kind === 'hz') { const hz = +rest.pop(); send({ cmd: 'rigHz', device: rest.join(':'), hz }); }
          else send({ cmd: 'rigTweak', id: rest.join(':') });
        });
        const acts = el('div', 'sec-acts'); acts.append(btn); body.append(acts);
      }
      li.append(el('span', 'dot'), el('strong', null, c.title), body);
      return li;
    }));
  }
  function rigDone(r) {
    const box = $('gm-rig-msg'); box.replaceChildren(el('span', null, r.message)); box.className = `result ${r.ok ? 'ok' : 'bad'}`;
    clearInterval(rigKeepT);
    if (r.pending) {
      let n = 15; const b = el('button', 'btn primary', `Se ve bien (${n})`);
      b.addEventListener('click', () => { clearInterval(rigKeepT); send({ cmd: 'rigKeep' }); });
      box.append(' ', b);
      rigKeepT = setInterval(() => { n -= 1; b.textContent = `Se ve bien (${n})`; if (n <= 0) clearInterval(rigKeepT); }, 1000);
    }
  }

  function renderFortnite(f) {
    const box = $('gm-fn');
    if (!f.found) { box.replaceChildren(el('p', 'empty', 'No encontré Fortnite en este PC. Si lo tienes, ábrelo una vez para que cree su configuración.')); return; }
    const head = el('div', 'gm-fn-head');
    head.append(el('p', null, `Modo de render: ${f.mode}.`));
    if (f.profileApplied) head.append(el('p', null, 'Perfil competitivo de Vigía puesto.'));
    if (f.running) head.append(el('p', 'src why', 'Fortnite está abierto. Ciérralo para cambiar algo: si no, al salir guarda encima.'));
    if (f.readOnly) head.append(el('p', 'src why', 'El archivo está en «solo lectura»: Fortnite no puede guardar lo que cambies dentro del juego. Cualquier cambio de aquí lo quita.'));
    const acts = el('div', 'gm-ctl');
    const prof = el('button', 'btn primary', 'Aplicar perfil competitivo');
    prof.hidden = f.profileApplied;
    prof.disabled = f.running;
    prof.addEventListener('click', () => { busyBtn(prof, true, 'Aplicando…'); send({ cmd: 'fortniteApply', profile: true }); });
    const perf = f.mode.startsWith('Rendimiento');
    const mode = el('button', 'btn', perf ? 'Cambiar a DirectX 12' : 'Cambiar a modo Rendimiento');
    mode.disabled = f.running;
    mode.addEventListener('click', () => { busyBtn(mode, true, 'Cambiando…'); send({ cmd: 'fortniteApply', profile: false, mode: perf ? 'dx12' : 'perf' }); });
    acts.append(prof, mode);
    if (f.readOnly && f.profileApplied) {
      const un = el('button', 'btn', 'Permitir que Fortnite guarde');
      un.disabled = f.running;
      un.addEventListener('click', () => { busyBtn(un, true, 'Cambiando…'); send({ cmd: 'fortniteApply', profile: false }); });
      acts.append(un);
    }
    const tip = el('p', 'src', perf
      ? 'Rendimiento suele dar más FPS cuando el procesador es el límite; DirectX 12 se ve mejor. Mide una partida con cada uno y quédate con el que gane.'
      : 'El modo Rendimiento quita detalle gráfico a cambio de FPS. Mide una partida antes y otra después de cambiar para saber si te conviene.');
    // dos bloques compactos: lo que toca el perfil y lo que eliges tú; solo se marca lo que cambiaría
    const table = (title, rows) => {
      const sec = el('div', 'gm-tbl');
      sec.append(el('h3', null, title));
      const dl = el('dl');
      rows.forEach((s) => {
        const dd = el('dd', s.want != null && !s.ok ? 'chg' : null, s.want != null && !s.ok ? `${fnValue(s)} → ${fnValue({ ...s, value: s.want })}` : fnValue(s));
        dl.append(el('dt', null, s.label), dd);
      });
      sec.append(dl);
      return sec;
    };
    const rx = f.settings.find((s) => s.key === 'ResolutionSizeX'), ry = f.settings.find((s) => s.key === 'ResolutionSizeY');
    const list = f.settings.filter((s) => s !== ry).map((s) => (s === rx && ry ? { ...s, label: 'Resolución', value: `${rx.value} × ${ry.value}`, key: 'res' } : s));
    const mine = list.filter((s) => s.want != null), yours = list.filter((s) => s.want == null);
    const pending = mine.filter((s) => !s.ok).length;
    const ul = el('div', 'gm-tbls');
    ul.append(table(pending ? `Perfil competitivo: ${pending} por cambiar` : 'Perfil competitivo: todo en su sitio', mine), table('Lo eliges tú', yours));
    box.replaceChildren(head, acts, tip, ul,
      el('p', 'src', 'Vigía solo cambia el archivo de configuración de Fortnite en tu usuario, con el juego cerrado y con copia en «Copias de seguridad». No toca distancia de visión, texturas, resolución ni tu límite de FPS.'));
  }
  $('gm-reload').addEventListener('click', () => { busyBtn($('gm-reload'), true, 'Recargando…'); send({ cmd: 'gamesGet' }); });
  $('gm-measure').addEventListener('click', () => { $('gm-result').textContent = ''; send({ cmd: 'fpsStart', seconds: +$('gm-dur').value }); renderFpsState({ active: true, phase: 'waiting', target: +$('gm-dur').value, done: 0 }); });
  $('gm-cancel').addEventListener('click', () => send({ cmd: 'fpsCancel' }));

  // ---------- copias de seguridad ----------
  const bkKind = { juegos: 'Juegos', ajustes: 'Ajustes', inicio: 'Inicio de Windows', pantalla: 'Pantalla', apps: 'Apps', restauracion: 'Restauración' };
  function bkWhen(s) {
    const d = new Date(s);
    return d.toLocaleDateString(window.vigiaI18n.locale(), { day: 'numeric', month: 'short' }) + ', ' + d.toLocaleTimeString(window.vigiaI18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }
  function bkArm(b, idle, armedText, onGo) {
    let armed = false;
    b.addEventListener('click', () => {
      if (!armed) { armed = true; b.textContent = armedText; b.classList.add('primary'); setTimeout(() => { if (armed) { armed = false; b.textContent = idle; b.classList.remove('primary'); } }, 4000); return; }
      armed = false; onGo();
    });
  }
  // la copia fija de cómo estaba todo antes de Vigía
  function renderOriginal(o) {
    const box = $('bk-original');
    if (!o) { box.replaceChildren(el('p', 'src', 'Guardando cómo está tu PC ahora, antes de cualquier cambio…')); return; }
    const n = o.counts || {};
    const parts = [];
    if (n.tweak) parts.push(plural(n.tweak, 'ajuste de Windows', 'ajustes de Windows'));
    const st = (n.reg || 0) + (n.task || 0);
    if (st) parts.push(plural(st, 'valor de inicio de Windows', 'valores de inicio de Windows'));
    if (n.display) parts.push('la resolución');
    if (n.appx) parts.push(plural(n.appx, 'app instalada', 'apps instaladas'));
    const body = el('div');
    body.append(el('strong', null, 'Cómo estaba tu PC antes de Vigía'),
      el('p', null, `Guardada el ${bkWhen(o.at)}, antes del primer cambio. Esta copia nunca se borra.`),
      el('p', 'src'));
    // cada parte en su nodo: así se traduce sola («35» + «ajustes de Windows»)
    parts.forEach((x, i) => { if (i) body.lastChild.append(' · '); body.lastChild.append(el('span', null, x)); });
    if (o.restorePoint) body.append(el('p', 'src', 'Con punto de restauración de Windows del mismo momento.'));
    if (o.restoredAt) body.append(el('p', 'src', `Usada el ${bkWhen(o.restoredAt)}.`));
    const b = el('button', 'btn', 'Volver a como estaba');
    bkArm(b, 'Volver a como estaba', '¿Seguro? Deshace todo lo de Vigía', () => { busyBtn(b, true, 'Restaurando…'); send({ cmd: 'backupRestore', id: o.id }); });
    box.replaceChildren(body, b);
  }
  function renderBackups(all) {
    busyBtn($('bk-reload'), false, 'Volver a leer');
    renderOriginal(all.find((c) => c.kind === 'original'));
    const list = all.filter((c) => c.kind !== 'original');
    $('bk-sub').textContent = list.length
      ? `${plural(list.length, 'copia guardada', 'copias guardadas')}. Antes de cada cambio, Vigía guarda cómo estaba todo; restaurar también guarda una copia, por si quieres deshacerlo.`
      : 'Todavía no hay copias de cambios. Se crea una antes de cada ajuste, apagado de inicio, cambio de pantalla o app quitada.';
    if (!list.length) { $('bk-list').replaceChildren(el('li', 'empty', 'Aún no has cambiado nada con Vigía.')); return; }
    $('bk-list').replaceChildren(...list.map((c) => {
      const li = el('li', 'row bk');
      const body = el('div');
      const lines = c.lines.slice(0, 4).join(' · ') + (c.lines.length > 4 ? ` y ${c.lines.length - 4} más` : '');
      body.append(el('strong', null, c.title), el('p', null, `${bkKind[c.kind] || c.kind} · ${bkWhen(c.at)}`));
      if (!c.title.endsWith(lines)) body.append(Object.assign(el('p', 'src cmd', lines), { title: c.lines.join('\n') }));
      if (c.restorePoint) body.append(el('p', 'src', 'Con punto de restauración de Windows.'));
      if (c.restoredAt) body.append(el('p', 'src', `Restaurada el ${bkWhen(c.restoredAt)}.`));
      const b = el('button', 'btn', 'Restaurar');
      bkArm(b, 'Restaurar', '¿Seguro? Restaurar', () => { busyBtn(b, true, 'Restaurando…'); send({ cmd: 'backupRestore', id: c.id }); });
      li.append(el('span', `state${c.restoredAt ? '' : ' applied'}`, c.restoredAt ? 'Usada' : 'Guardada'), body, b);
      return li;
    }));
  }
  $('bk-reload').addEventListener('click', () => { busyBtn($('bk-reload'), true, 'Leyendo…'); send({ cmd: 'backupsList' }); });

  // ---------- resolución estirada ----------
  let dsTimer = null;
  function renderDisplay(d) {
    const n = d.native, c = d.current;
    const res = (m) => `${m.width} × ${m.height}`;
    $('ds-restore').hidden = !d.saved;
    if (!n || !c) { $('ds-sub').textContent = 'No pude leer la pantalla principal.'; return; }
    $('ds-sub').textContent = d.stretched
      ? `Ahora: ${res(c)} estirada a ${c.hz} Hz. Tu resolución normal es ${res(d.saved)}.`
      : c.width * c.height < n.width * n.height
        ? `Ahora: ${res(c)} a ${c.hz} Hz; ya la tienes por debajo de la nativa de tu pantalla (${res(n)}). Menos píxeles dan más FPS solo si la tarjeta gráfica va al límite.`
        : `Ahora: ${res(c)} a ${c.hz} Hz. Menos píxeles dan más FPS solo si la tarjeta gráfica va al límite; si el que limita es el procesador, casi no cambia. La imagen se ve más ancha.`;
    $('ds-mode').replaceChildren(...d.options.map((m) => {
      const o = el('option', null, `${res(m)}${m.width === 1440 && m.height === 1080 ? ' · la más usada' : ''}`); o.value = `${m.width}x${m.height}`;
      if (m.width === c.width && m.height === c.height) o.selected = true;
      return o;
    }));
    if (!d.options.length) $('ds-sub').textContent += ' La pantalla no ofrece resoluciones más estrechas.';
    const pick = d.options.find((m) => m.width === 1440 && m.height === 1080);
    if (pick && !d.stretched) $('ds-mode').value = '1440x1080';
    $('ds-apply').disabled = !d.options.length;
    $('ds-apply').textContent = d.stretched ? 'Cambiar' : 'Estirar';
  }
  function dsConfirm(show) {
    clearInterval(dsTimer);
    $('ds-confirm').hidden = !show;
    if (!show) return;
    let s = 15; $('ds-count').textContent = s;
    dsTimer = setInterval(() => {
      s -= 1; $('ds-count').textContent = s;
      if (s <= 0) { dsConfirm(false); send({ cmd: 'displayRestore' }); }
    }, 1000);
  }
  $('ds-apply').addEventListener('click', () => {
    const [w, h] = $('ds-mode').value.split('x').map(Number);
    busyBtn($('ds-apply'), true, 'Cambiando…');
    send({ cmd: 'displayStretch', w, h });
  });
  $('ds-restore').addEventListener('click', () => { dsConfirm(false); send({ cmd: 'displayRestore' }); });
  $('ds-undo').addEventListener('click', () => { dsConfirm(false); send({ cmd: 'displayRestore' }); });
  $('ds-keep').addEventListener('click', () => { dsConfirm(false); $('tl-result').textContent = 'Resolución estirada guardada. «Volver a la normal» la deja como estaba.'; });

  // primera visita a cada sección: cargar sus datos
  const loaded = new Set();
  document.querySelectorAll('.nav').forEach((b) => b.addEventListener('click', () => {
    const v = b.dataset.view;
    if (loaded.has(v)) return;
    loaded.add(v);
    if (v === 'health') $('h-scan').click();
    else if (v === 'opt') send({ cmd: 'optStatus', analyze: false });
    else if (v === 'diag') $('d-run').click();
    else if (v === 'sug') { if (!sugDone) $('s-run').click(); }
    else if (v === 'storage' && !targets.length) $('scan').click();
    else if (v === 'mem') send({ cmd: 'settingsGet' });
    else if (v === 'tweaks') send({ cmd: 'tweaksList' });
    else if (v === 'apps') send({ cmd: 'bloatList' });
    else if (v === 'bios') send({ cmd: 'biosGet' });
    else if (v === 'tools') { send({ cmd: 'toolsList' }); send({ cmd: 'displayStatus' }); }
    else if (v === 'startup') send({ cmd: 'startupList' });
    else if (v === 'games') send({ cmd: 'gamesGet' });
    else if (v === 'backups') send({ cmd: 'backupsList' });
    else if (v === 'support') send({ cmd: 'supportGet' });
  }));
  // para --snap: abrir una sección
  // 'opt-run' = abrir Optimizar y lanzar la primera unidad (prueba de extremo a extremo)
  window.vigiaSnap = (spec) => {
    const [v, lang, dbg] = spec.split('@');   // 'settings@en' = Configuración en inglés; 'settings@en@dbg' añade el diagnóstico
    if (lang) window.vigiaI18n.set(lang);
    if (lang && dbg) setTimeout(() => {   // diagnóstico de idioma en la captura
      const I = window.vigiaI18n, left = [];
      const it = document.createTreeWalker(document.querySelector('.view:not([hidden])') || document.body, NodeFilter.SHOW_TEXT);
      for (let n = it.nextNode(); n; n = it.nextNode()) { const s = n.nodeValue.trim(); if (s && I.t(s) !== s) left.push(s.slice(0, 30) + ' ' + I.debug(n).slice(0, 80) + ' ' + n.parentElement.closest('[translate]')?.tagName); }
      const pre = document.createElement('pre'); pre.setAttribute('translate', 'no');
      pre.textContent = 'I18N ' + JSON.stringify({ lang: I.lang, errors: I.errors, left: left.slice(0, 15) });
      (document.querySelector('.view:not([hidden])') || document.body).append(pre);
    }, 9000);
    if (v === 'welcome') { tourStart(true); return; }
    if (v.startsWith('tour')) { tourFrom = 'monitor'; tourShow(Number(v.split('-')[1] || 1) - 1); return; }
    const [page, anchor] = v.split('#');   // 'games#gm-fn' = abrir Juegos y bajar hasta Fortnite
    const [view, act] = page.split('-');
    document.querySelector(`.nav[data-view="${view}"]`)?.click();
    if (anchor) setTimeout(() => $(anchor)?.scrollIntoView(), 2500);
    if (act === 'run' && view === 'opt') setTimeout(() => document.querySelector('.vol .btn')?.click(), 2500);
    if (act === 'run' && view === 'mem') setTimeout(() => $('m-free').click(), 2500);
    if (act === 'excl') setTimeout(() => { document.querySelector('.excluded').open = true; }, 500);
  };

  // ---------- actualizaciones (las instala Puente) ----------
  let upAsked = false;
  function renderUpdate(u) {
    $('up-check').textContent = `Versión ${u.current}`;
    $('up-check').disabled = false;
    $('st-ver').textContent = `Tienes la versión ${u.current}`;
    busyBtn($('st-check'), false, 'Buscar actualizaciones');
    busyBtn($('st-run'), false, 'Actualizar ahora');
    busyBtn($('up-run'), false, 'Actualizar');
    const box = $('update');
    if (u.available) {
      box.hidden = false; $('up-run').hidden = false;
      $('up-text').textContent = `Hay una versión nueva: ${u.latest}. Vigía se cierra, se actualiza y vuelve a abrir sola.`;
      $('st-new').hidden = false;
      $('st-new-title').textContent = `Versión nueva: ${u.latest}`;
      $('st-notes').textContent = (u.notes || '').trim();
      $('st-state').textContent = u.error || 'Vigía baja la versión nueva, comprueba la firma, se cierra, se actualiza y vuelve a abrir sola.';
      $('st-state').classList.toggle('bad', !!u.error);
    } else {
      $('st-new').hidden = true;
      if (upAsked) {
        $('st-state').textContent = u.error ? `No pude mirar si hay versión nueva: ${u.error}` : `Estás en la última versión (${u.current}).`;
        $('st-state').classList.toggle('bad', !!u.error);
      }
    }
    upAsked = false;
  }
  const goSettings = () => document.querySelector('.nav[data-view="settings"]').click();
  $('up-check').addEventListener('click', () => { goSettings(); $('st-check').click(); });
  $('st-check').addEventListener('click', () => { upAsked = true; busyBtn($('st-check'), true, 'Buscando…'); send({ cmd: 'updateCheck' }); });
  const runUpdate = () => { busyBtn($('st-run'), true, 'Preparando…'); busyBtn($('up-run'), true, 'Preparando…'); send({ cmd: 'updateRun' }); };
  $('st-run').addEventListener('click', runUpdate);
  $('up-run').addEventListener('click', runUpdate);
  send({ cmd: 'version' });

  // ---------- configuración: idioma y búsqueda automática ----------
  const I = window.vigiaI18n;
  Object.entries(I.langs).forEach(([code, name]) => { const o = el('option', null, name); o.value = code; o.translate = false; $('st-lang').append(o); });
  function applyLang(pref) {
    try { localStorage.setItem('vigia-lang', pref); } catch { /* sin almacenamiento */ }
    I.set(pref);
  }
  try { $('st-lang').value = localStorage.getItem('vigia-lang') || 'auto'; } catch { /* nada */ }
  $('st-lang').addEventListener('change', () => {
    applyLang($('st-lang').value);
    if (cfg) { cfg.lang = $('st-lang').value; send({ cmd: 'settingsSet', cfg }); }
  });
  $('st-auto').addEventListener('change', () => { if (cfg) { cfg.updateCheck = $('st-auto').checked; send({ cmd: 'settingsSet', cfg }); } });
  send({ cmd: 'settingsGet' });

  // ---------- ayuda y apoyo ----------
  const goView = (v) => document.querySelector(`.nav[data-view="${v}"]`)?.click();
  document.querySelectorAll('.hp-go').forEach((b) => b.addEventListener('click', () => goView(b.dataset.go)));
  const DOWNLOAD = 'https://jcperezdev.github.io/vigia-releases/';
  $('sp-star').addEventListener('click', () => send({ cmd: 'openUrl', url: 'repo' }));
  $('sp-issue').addEventListener('click', () => send({ cmd: 'openUrl', url: 'https://github.com/JcperezDev/vigia-releases/issues' }));
  $('sp-share').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(DOWNLOAD); $('sp-result').textContent = 'Enlace copiado. Pégalo donde quieras.'; }
    catch { $('sp-result').textContent = `No pude copiar. El enlace es ${DOWNLOAD}`; }
  });
  function renderSupport(p) {
    const box = $('sp-links');
    box.replaceChildren();
    if (!p.links.length) {
      box.append(el('p', 'empty', p.error ? 'No pude cargar los enlaces para donar. Revisa la conexión y vuelve a abrir esta sección.' : 'Los enlaces para donar estarán aquí muy pronto.'));
      return;
    }
    p.links.forEach((l, i) => {
      const b = el('button', i === 0 ? 'btn primary sp-btn' : 'btn sp-btn', l.name);
      b.addEventListener('click', () => send({ cmd: 'openUrl', url: l.url }));
      box.append(b);
    });
    box.append(el('p', 'sp-safe', 'Se abre en tu navegador. Vigía no ve ni guarda nada del pago.'));
  }

  // ---------- recorrido ----------
  // La primera vez sale una bienvenida; el recorrido va por las secciones de verdad, una por paso.
  const TOUR = [
    { view: 'monitor', at: '.rail', title: 'Todo está en esta barra',
      text: 'Arriba, lo que miras. En «Mejorar», lo que cambia tu PC. En «Revisar», lo que solo lee. Nada cambia hasta que pulsas un botón.' },
    { view: 'monitor', at: '.chart-wrap', title: 'Monitor',
      text: 'Uso y temperatura de CPU, tarjeta gráfica y RAM. Si minimizas Vigía, deja de dibujar y casi no gasta.' },
    { view: 'sug', at: '#view-sug .head', title: 'Empieza por Sugerencias',
      text: 'Revisa la conexión, la pantalla, la memoria y los discos, y te dice qué mejorar y por qué. Por ejemplo: si tu Wi‑Fi pierde paquetes, te sugiere un cable. No cambia nada por su cuenta.' },
    { view: 'mem', at: '#view-mem .head', title: 'Memoria',
      text: '«Liberar ahora» devuelve RAM sin cerrar tus programas. Además se libera sola cuando el uso pasa del 60 %; ese número lo cambias más abajo, en «Automático».' },
    { view: 'tweaks', at: '#view-tweaks .head', title: 'Ajustes de Windows',
      text: 'Cada ajuste dice qué hace en palabras simples. Marca los que quieras y pulsa Aplicar. Los que tienen riesgo vienen apagados y avisados.' },
    { view: 'startup', at: '#view-startup .head', title: 'Apps e Inicio de Windows',
      text: 'En Apps de Windows quitas lo que viene de fábrica y no usas. Aquí apagas lo que arranca solo al encender el PC, sin borrarlo.' },
    { view: 'backups', at: '#bk-original', title: 'Todo tiene vuelta atrás',
      text: 'Antes de cada cambio, Vigía guarda cómo estaba. «Volver a como estaba» deja tu PC como antes de instalar Vigía.' },
    { view: 'tools', at: '#ds-box', title: 'Resolución estirada',
      text: 'Estira la pantalla para jugar. Si se ve mal y no confirmas, vuelve sola en 15 segundos.' },
    { view: 'help', at: '.rail-end', title: 'Atajos junto al reloj',
      text: 'Si cierras la ventana, Vigía se queda junto al reloj, en «Mostrar iconos ocultos». Con clic derecho optimizas, liberas memoria o cambias la resolución sin abrirla. Este recorrido está siempre en Ayuda.' },
  ];
  const WELCOME = { title: 'Bienvenido a Vigía',
    text: 'Vigía mide tu PC, te dice qué mejorar y hace los cambios contigo, siempre con copia para volver atrás. El recorrido dura un minuto.' };
  let tourAt = -1, tourFrom = null;
  const tourRO = new ResizeObserver(() => { if (tourAt >= 0 && !$('tour').hidden) tourPlace(); });   // las secciones cargan sus datos después
  const tourSeen = () => { try { return localStorage.getItem('vigia-tour') === 'done'; } catch { return true; } };
  const tourMark = () => { try { localStorage.setItem('vigia-tour', 'done'); } catch { /* sin almacenamiento */ } };

  function tourPlace() {
    const hole = $('tour-hole'), card = $('tour-card');
    const step = TOUR[tourAt];
    const t = step && document.querySelector(step.at);
    if (!t) { hole.hidden = true; card.classList.add('is-center'); card.style.left = card.style.top = ''; return; }
    const vw = innerWidth, vh = innerHeight, pad = 6;
    const r0 = t.getBoundingClientRect();
    const r = { left: Math.max(r0.left - pad, 8), top: Math.max(r0.top - pad, 8), right: Math.min(r0.right + pad, vw - 8), bottom: Math.min(r0.bottom + pad, vh - 8) };
    hole.hidden = false;
    Object.assign(hole.style, { left: `${r.left}px`, top: `${r.top}px`, width: `${r.right - r.left}px`, height: `${r.bottom - r.top}px` });
    card.classList.remove('is-center');
    const cw = card.offsetWidth, ch = card.offsetHeight, gap = 20;
    let x, y;
    if (r.right + gap + cw <= vw - 12) { x = r.right + gap; y = r.top; }
    else if (r.bottom + gap + ch <= vh - 12) { x = r.left; y = r.bottom + gap; }
    else if (r.left - gap - cw >= 12) { x = r.left - gap - cw; y = r.top; }
    else { x = r.left + 24; y = r.top + 24; }   // el destino ocupa casi toda la ventana: la tarjeta va encima
    card.style.left = `${Math.min(Math.max(x, 12), vw - cw - 12)}px`;
    card.style.top = `${Math.min(Math.max(y, 12), vh - ch - 12)}px`;
  }

  function tourShow(i) {
    tourAt = i;
    const welcome = i < 0, last = i === TOUR.length - 1;
    const step = welcome ? WELCOME : TOUR[i];
    if (!welcome) goView(step.view);
    $('tour').hidden = false;
    $('tour-title').textContent = step.title;
    $('tour-text').textContent = step.text;
    $('tour-count').textContent = welcome ? '' : `${i + 1} de ${TOUR.length}`;
    $('tour-prev').hidden = welcome || i === 0;
    $('tour-skip').textContent = welcome ? 'Lo exploro solo' : 'Saltar';
    $('tour-skip').style.visibility = last ? 'hidden' : '';
    $('tour-next').textContent = welcome ? 'Ver el recorrido' : last ? 'Terminar' : 'Siguiente';
    if (welcome) { $('tour-hole').hidden = true; $('tour-card').classList.add('is-center'); $('tour-card').style.left = $('tour-card').style.top = ''; }
    tourRO.disconnect();
    if (welcome) { /* tarjeta centrada */ }
    else { const t = document.querySelector(step.at); t?.scrollIntoView({ block: 'nearest' }); if (t) tourRO.observe(t); requestAnimationFrame(tourPlace); }
    $('tour-next').focus();
  }
  function tourEnd() {
    tourMark();
    $('tour').hidden = true; tourAt = -1; tourRO.disconnect();
    if (tourFrom) { goView(tourFrom); tourFrom = null; }
  }
  function tourStart(welcome) {
    tourFrom = document.querySelector('.nav.is-active')?.dataset.view || 'monitor';
    tourShow(welcome ? -1 : 0);
  }
  $('tour-next').addEventListener('click', () => (tourAt === TOUR.length - 1 ? (tourFrom = null, tourEnd()) : tourShow(tourAt + 1)));
  $('tour-prev').addEventListener('click', () => tourShow(Math.max(tourAt - 1, 0)));
  $('tour-skip').addEventListener('click', tourEnd);
  $('hp-tour').addEventListener('click', () => tourStart(false));
  document.addEventListener('keydown', (e) => {
    if ($('tour').hidden) return;
    if (e.key === 'Escape') tourEnd();
    else if (e.key === 'ArrowRight' && tourAt >= 0) $('tour-next').click();
    else if (e.key === 'ArrowLeft' && tourAt > 0) $('tour-prev').click();
    else if (e.key === 'Tab') {   // el foco no sale de la tarjeta
      const f = [...$('tour-card').querySelectorAll('button')].filter((b) => !b.hidden);
      const k = f.indexOf(document.activeElement);
      if (e.shiftKey && k <= 0) { e.preventDefault(); f[f.length - 1].focus(); }
      else if (!e.shiftKey && k === f.length - 1) { e.preventDefault(); f[0].focus(); }
    }
  });
  addEventListener('resize', () => { if (!$('tour').hidden && tourAt >= 0) tourPlace(); });
  window.addEventListener('vigia-lang', () => { if (!$('tour').hidden && tourAt >= 0) requestAnimationFrame(tourPlace); });
  setTimeout(() => { if (!window.__vigiaSnap && !tourSeen()) tourStart(true); }, 900);

  // ---------- mensajes del backend ----------
  if (host) host.addEventListener('message', (e) => {
    const { type, payload } = e.data;
    if (type === 'telemetry') render(payload);
    else if (type === 'healthScan') renderHealth(payload);
    else if (type === 'diagRun') renderDiag(payload);
    else if (type === 'sec') renderSec(payload);
    else if (type === 'secProgress') { const m = $('sec-msg'); if (m) m.textContent = payload; }
    else if (type === 'secDone') { secBusy = false; const m = $('sec-msg'); if (m) { m.textContent = payload.message; m.dataset.keep = '1'; m.className = `sec-msg ${payload.ok ? 'ok' : 'bad'}`; } }
    else if (type === 'suggest') renderSug(payload);
    else if (type === 'support') renderSupport(payload);
    else if (type === 'optStatus') renderOpt(payload);
    else if (type === 'memStatus') renderMem(payload);
    else if (type === 'memFreed') renderFreed(payload);
    else if (type === 'settings') {
      cfg = payload.cfg;
      $('st-auto').checked = cfg.updateCheck;
      if (cfg.lang && $('st-lang').value !== cfg.lang) { $('st-lang').value = cfg.lang; applyLang(cfg.lang); }
      $('m-auto').checked = cfg.memAuto; $('m-thr').value = cfg.memAutoThreshold; $('m-int').value = cfg.memAutoIntervalMin;
    }
    else if (type === 'tweaks') { renderTweaks(payload); apNote(payload.items); }
    else if (type === 'bios') renderBios(payload);
    else if (type === 'biosDone') { $('bi-result').textContent = payload.message; $('bi-result').classList.toggle('bad', !payload.ok); }
    else if (type === 'isoSource') renderIsoSource(payload);
    else if (type === 'isoOutput') { isoOut = payload.path; $('iso-out').textContent = `Se guarda como ${isoOut}`; isoReady(); }
    else if (type === 'isoProgress') isoProgress(payload);
    else if (type === 'isoDone') isoDone(payload);
    else if (type === 'tweaksDone') renderTweaksDone(payload);
    else if (type === 'utilidad-externaRp') {
      busyBtn($('tw-utilidad-externa'), false, 'Abrir utilidad externa');
      const r = $('tw-result'); r.textContent = '';
      if (payload.opened) r.textContent = payload.message ? `${payload.message} utilidad externa se abre en otra ventana.` : 'utilidad externa se abre en otra ventana.';
      else {
        r.append(`${payload.message} Por eso no abrí utilidad externa: sin ese punto no hay forma fácil de deshacer lo que cambie. `);
        if (payload.off) {
          const on = el('button', 'btn primary sp-inline', 'Activar la protección y abrir');
          on.addEventListener('click', () => { busyBtn($('tw-utilidad-externa'), true, 'Creando punto de restauración…'); r.textContent = ''; send({ cmd: 'utilidad-externa', enable: true }); });
          r.append(on);
        }
        const go = el('button', 'btn sp-inline', 'Abrir igual, sin punto');
        go.addEventListener('click', () => { go.disabled = true; send({ cmd: 'utilidad-externa', force: true }); });
        r.append(go);
      }
    }
    else if (type === 'bloat') renderBloat(payload);
    else if (type === 'bloatDone') {
      busyBtn($('ap-remove'), false, 'Quitar seleccionadas');
      const bad = payload.filter((r) => !r.ok);
      $('ap-result').textContent = bad.length ? `No se pudieron quitar: ${bad.map((r) => `${r.id} (${r.error})`).join('; ')}.` : `${plural(payload.length, 'app quitada', 'apps quitadas')}.`;
    }
    else if (type === 'tools') renderTools(payload);
    else if (type === 'startup') renderStartup(payload);
    else if (type === 'games') { busyBtn($('gm-reload'), false, 'Recargar'); renderRig(payload.rig); renderFps(payload.fps); renderFortnite(payload.fortnite); }
    else if (type === 'rigDone') rigDone(payload);
    else if (type === 'gameDone') $('gm-result').textContent = payload.message;
    else if (type === 'fpsState') renderFpsState(payload);
    else if (type === 'backups') renderBackups(payload);
    else if (type === 'backupDone') {
      $('bk-result').textContent = payload.message + (payload.errors.length ? ` ${payload.errors.join('; ')}` : '');
      $('bk-result').classList.toggle('bad', !payload.ok);
    }
    else if (type === 'startupDone') {
      $('su-result').textContent = payload.ok ? '' : `No se pudo cambiar: ${payload.message}`;
      $('su-result').classList.toggle('bad', !payload.ok);
    }
    else if (type === 'display') { busyBtn($('ds-apply'), false); renderDisplay(payload); }
    else if (type === 'displayDone') {
      $('tl-result').textContent = payload.message;
      $('tl-result').classList.toggle('bad', !payload.ok);
      dsConfirm(payload.ok && payload.pending);
    }
    else if (type === 'update') renderUpdate(payload);
    else if (type === 'toolDone') {
      const b = document.querySelector(`[data-tool="${CSS.escape(payload.id)}"]`);
      if (payload.id === 'restorepoint' && b) busyBtn(b, false, 'Ejecutar');
      $('tl-result').textContent = payload.ok ? (payload.message || '') : `No se pudo abrir: ${payload.message}`;
      $('tl-result').classList.toggle('bad', !payload.ok);
    }
    else if (type === 'optProgress') {
      $('op-line').textContent = payload.line;
      if (payload.pct != null) { $('op-bar').parentElement.classList.remove('indet'); $('op-bar').style.width = `${payload.pct}%`; }
    } else if (type === 'optDone') {
      optBusy = false; $('oprun').hidden = true;
      $('op-result').textContent = payload.ok
        ? `${payload.letter} optimizada en ${fmt(payload.seconds, 0)} s.`
        : `${payload.letter}: ${payload.summary}`;
    }
    else if (type === 'cleanScan') {
      $('scan').classList.remove('busy'); $('scan').textContent = 'Volver a analizar';
      targets = payload; renderTargets();
      if (lastRun) { $('clean-total').textContent = lastRun; lastRun = null; }
    } else if (type === 'cleanRun') {
      $('run').textContent = 'Limpiar seleccionado';
      const extra = payload.skipped ? `; ${payload.skipped} en uso se saltaron` : '';
      lastRun = `Liberados ${gb(payload.freedBytes)} (${payload.deleted} archivos${extra}).`;
      send({ cmd: 'cleanScan' });
    } else if (type === 'error') {
      $('notes').hidden = false; $('notes').textContent = `Error: ${payload}`;
    }
  });

  resize();
})();
