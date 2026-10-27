(() => {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const host = window.chrome && window.chrome.webview;
  const send = (msg) => host && host.postMessage(msg);

  const fmt = (v, d = 0, unit = '') => (v === null || v === undefined || Number.isNaN(v)) ? 'N/D'
    : `${Number(v).toLocaleString('es', { minimumFractionDigits: d, maximumFractionDigits: d })}${unit}`;
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
    lastTs = Date.now();
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

    $('privilege').innerHTML = s.elevated
      ? '<strong>Modo administrador</strong>Sensores completos.'
      : '<strong>Modo estándar</strong>Ejecuta como administrador para ver temperaturas.';
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
    ctx.fillText('ahora', w - 40, h - 4);

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
      ctx.fillText('Temperatura de CPU no disponible en modo estándar.', padL + 12, padT + 20);
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
  const nf = new Intl.NumberFormat('es', { useGrouping: 'always', maximumFractionDigits: 0 });
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
  $('d-run').addEventListener('click', () => { busyBtn($('d-run'), true, 'Revisando…'); send({ cmd: 'diagRun' }); });
  function renderDiag(r) {
    busyBtn($('d-run'), false, 'Volver a revisar');
    const s = r.system;
    const up = s.uptimeHours >= 48 ? `${Math.round(s.uptimeHours / 24)} días` : `${fmt(s.uptimeHours, 0)} h`;
    $('sysinfo').replaceChildren(stat('Sistema', `${s.os} ${s.build}`), stat('Placa base', s.board || 'N/D'), stat('BIOS', s.bios || 'N/D'), stat('Encendido', up));
    [...$('sysinfo').querySelectorAll('dd')].forEach((d) => { d.title = d.textContent; });
    const count = (l) => r.items.filter((i) => i.level === l).length;
    $('tally').replaceChildren(...['bad', 'warn', 'info', 'ok'].filter((l) => count(l)).map((l) => el('span', `pill lv-${l}`, `${count(l)} ${LVN[l][count(l) === 1 ? 0 : 1]}`)));
    $('diag-sub').textContent = `Registro de eventos de Windows, últimos ${r.days} días. Solo lectura. Revisado a las ${new Date(r.ts).toLocaleTimeString('es')}.`;
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


  // ---------- memoria ----------
  const GBf = (v) => `${(v ?? 0).toLocaleString('es', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} GB`;
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
    const shown = list.filter((a) => a.installed);
    $('ap-remove').disabled = true;
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

  // ---------- herramientas ----------
  function renderTools(p) {
    $('tl-sub').textContent = `Programas de ${p.root}. Vigía solo los abre; no los copia ni los cambia.`;
    const groups = [...new Set(p.items.map((t) => t.group))];
    $('tl-groups').replaceChildren(...groups.map((g) => {
      const sec = el('section', 'tw-group'); sec.append(el('h2', null, g));
      const grid = el('div', 'tools');
      p.items.filter((t) => t.group === g).forEach((t) => {
        const card = el('div', 'tool');
        const b = el('button', 'btn', 'Abrir'); b.disabled = !t.found;
        b.addEventListener('click', () => send({ cmd: 'toolOpen', id: t.id }));
        card.append(el('strong', null, t.name), b, el('p', null, t.found ? t.detail : 'No está en la carpeta.'));
        if (t.warning) card.append(el('p', 'warn', t.warning));
        grid.append(card);
      });
      sec.append(grid);
      return sec;
    }));
  }

  // primera visita a cada sección: cargar sus datos
  const loaded = new Set();
  document.querySelectorAll('.nav').forEach((b) => b.addEventListener('click', () => {
    const v = b.dataset.view;
    if (loaded.has(v)) return;
    loaded.add(v);
    if (v === 'health') $('h-scan').click();
    else if (v === 'opt') send({ cmd: 'optStatus', analyze: false });
    else if (v === 'diag') $('d-run').click();
    else if (v === 'storage' && !targets.length) $('scan').click();
    else if (v === 'mem') send({ cmd: 'settingsGet' });
    else if (v === 'tweaks') send({ cmd: 'tweaksList' });
    else if (v === 'apps') send({ cmd: 'bloatList' });
    else if (v === 'tools') send({ cmd: 'toolsList' });
  }));
  // para --snap: abrir una sección
  // 'opt-run' = abrir Optimizar y lanzar la primera unidad (prueba de extremo a extremo)
  window.vigiaSnap = (v) => {
    const [view, act] = v.split('-');
    document.querySelector(`.nav[data-view="${view}"]`)?.click();
    if (act === 'run' && view === 'opt') setTimeout(() => document.querySelector('.vol .btn')?.click(), 2500);
    if (act === 'run' && view === 'mem') setTimeout(() => $('m-free').click(), 2500);
    if (act === 'excl') setTimeout(() => { document.querySelector('.excluded').open = true; }, 500);
  };

  // ---------- mensajes del backend ----------
  if (host) host.addEventListener('message', (e) => {
    const { type, payload } = e.data;
    if (type === 'telemetry') render(payload);
    else if (type === 'healthScan') renderHealth(payload);
    else if (type === 'diagRun') renderDiag(payload);
    else if (type === 'optStatus') renderOpt(payload);
    else if (type === 'memStatus') renderMem(payload);
    else if (type === 'memFreed') renderFreed(payload);
    else if (type === 'settings') {
      cfg = payload.cfg;
      $('m-auto').checked = cfg.memAuto; $('m-thr').value = cfg.memAutoThreshold; $('m-int').value = cfg.memAutoIntervalMin;
    }
    else if (type === 'tweaks') renderTweaks(payload);
    else if (type === 'tweaksDone') renderTweaksDone(payload);
    else if (type === 'utilidad-externaRp') { busyBtn($('tw-utilidad-externa'), false, 'Abrir utilidad externa'); $('tw-result').textContent = `${payload.message} utilidad externa se abre en otra ventana.`; }
    else if (type === 'bloat') renderBloat(payload);
    else if (type === 'bloatDone') {
      busyBtn($('ap-remove'), false, 'Quitar seleccionadas');
      const bad = payload.filter((r) => !r.ok);
      $('ap-result').textContent = bad.length ? `No se pudieron quitar: ${bad.map((r) => `${r.id} (${r.error})`).join('; ')}.` : `${plural(payload.length, 'app quitada', 'apps quitadas')}.`;
    }
    else if (type === 'tools') renderTools(payload);
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
