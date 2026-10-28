(() => {
  'use strict';
  const $ = (id) => document.getElementById(id);
  const host = window.chrome && window.chrome.webview;
  const send = (msg) => host && host.postMessage(msg);

  const fmt = (v, d = 0, unit = '') => (v === null || v === undefined || Number.isNaN(v)) ? 'N/D'
    : `${Number(v).toLocaleString(window.vigiaI18n.locale(), { minimumFractionDigits: d, maximumFractionDigits: d })}${unit}`;
  const gb = (b) => b >= 1073741824 ? `${(b / 1073741824).toFixed(2)} GB` : b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.round(b / 1024)} KB`;

  // ---------- navegación ----------
  document.querySelectorAll('.nav[data-view]').forEach((b) => b.addEventListener('click', () => {
    document.querySelectorAll('.nav[data-view]').forEach((n) => { n.classList.toggle('is-active', n === b); n.toggleAttribute('aria-current', n === b); });
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
    $('ram-lat').textContent = r.cl ? `≈ CL${r.cl}` : 'N/D';
    $('ram-lat').title = 'Calculado con la velocidad actual y los datos de fábrica del módulo. Windows no deja leer el CL exacto; míralo en la BIOS.';
    $('ram-temp').textContent = fmt(r.temp, 0, ' °C');

    // solo se avisa cuando falta algo; con todo bien no hace falta decirlo
    $('privilege').hidden = !!s.elevated;
    if (!s.elevated && !$('privilege').dataset.k) { $('privilege').replaceChildren(el('strong', null, 'Modo estándar'), 'Ejecuta como administrador para ver temperaturas.'); $('privilege').dataset.k = '1'; }
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
      const has = t.id === 'startup_left' ? t.files > 0 : t.bytes > 0;
      cb.checked = has; cb.disabled = !has; cb.dataset.id = t.id;
      cb.addEventListener('change', updateTotal);
      li.querySelector('.tname').textContent = t.name;
      li.querySelector('.path').textContent = t.id === 'startup_left' ? `${t.path} · ${t.files} entradas` : `${t.path} · ${t.files} archivos`;
      li.querySelector('.size').textContent = t.id === 'startup_left' ? '' : gb(t.bytes);
      return li;
    }));
    updateTotal();
  }
  function selected() { return [...document.querySelectorAll('#targets input:checked')].map((c) => c.dataset.id); }
  function updateTotal() {
    const ids = selected();
    const sum = targets.filter((t) => ids.includes(t.id)).reduce((a, t) => a + t.bytes, 0);
    $('clean-total').textContent = ids.length ? `${gb(sum)} se pueden liberar` : 'Nada seleccionado';
    $('run').disabled = !ids.length;
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

  function renderCrashes(list, days) {
    $('crashes').hidden = false;
    if (!list || !list.length) {
      $('crash-list').replaceChildren(el('li', 'crash lv-ok', `Nada se cerró de golpe en los últimos ${days} días.`));
      return;
    }
    const MS = { es: 'ene feb mar abr may jun jul ago sept oct nov dic', en: 'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec', pt: 'jan fev mar abr mai jun jul ago set out nov dez', fr: 'janv févr mars avr mai juin juil août sept oct nov déc', de: 'Jan. Feb. März Apr. Mai Juni Juli Aug. Sept. Okt. Nov. Dez.' };
    const when = (t) => { const d = new Date(t); const m = (MS[window.vigiaI18n.lang] || MS.es).split(' ')[d.getMonth()]; return `${d.getDate()} ${m}, ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`; };
    $('crash-list').replaceChildren(...list.map((c) => {
      const li = el('li', `crash lv-${c.level}`);
      const head = el('div', 'crash-head');
      head.append(el('span', 'dot'), el('strong', null, c.app), el('span', 'crash-kind', c.kind),
        el('span', 'crash-when', c.count > 1 ? `${c.count} veces · la última: ${when(c.last)}` : when(c.last)));
      const body = el('div', 'crash-body');
      body.append(el('p', null, c.why));
      if (c.hardware) body.append(el('p', 'crash-hw', 'A esa misma hora Windows registró un error de hardware (WHEA), así que lo más probable es que la causa sea el equipo y no el programa.'));
      body.append(el('p', 'crash-fix', `Qué hacer: ${c.fix}`));
      if (c.tech.length) {
        const det = el('details'); det.append(el('summary', null, 'Datos técnicos'));
        const ul = el('ul'); c.tech.forEach((x) => ul.append(el('li', null, x))); det.append(ul); body.append(det);
      }
      li.append(head, body);
      return li;
    }));
  }

  function renderDiag(r) {
    busyBtn($('d-run'), false, 'Volver a revisar');
    renderCrashes(r.crashes, r.days);
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
  const GO = { health: 'Abrir Salud de discos', storage: 'Abrir Almacenamiento', mem: 'Abrir Memoria', tweaks: 'Abrir Ajustes' };
  let sugDone = false;
  $('s-run').addEventListener('click', () => { busyBtn($('s-run'), true, 'Revisando…'); send({ cmd: 'suggest' }); });
  function renderSug(r) {
    sugDone = true;
    busyBtn($('s-run'), false, 'Volver a revisar');
    const n = r.items.filter((i) => i.level === 'warn' || i.level === 'bad').length;
    $('sug-count').hidden = !n; $('sug-count').textContent = n;
    $('sug-sub').textContent = 'Lo que se puede mejorar en tu PC.';
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
    twLaptop = !!p.laptop;
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
        if (t.risk !== 'bajo') src.append(el('span', `risk-${t.risk}`, `riesgo ${t.risk}`));
        if (t.reboot) { if (src.childNodes.length) src.append(' · '); src.append(el('span', null, 'pide reiniciar')); }
        if (src.childNodes.length) body.append(src);
        if (t.blocked) body.append(el('p', 'src why', t.blocked));
        li.append(cb, body, el('span', `state ${t.state}`, STATE[t.state] || t.state));
        ul.append(li);
      });
      sec.append(ul);
      return sec;
    }));
    const applied = tweaks.filter((t) => t.state === 'applied').length;
    const rp = p.restorePoints < 0 ? 'no se pudo leer los puntos de restauración' : plural(p.restorePoints, 'punto de restauración', 'puntos de restauración');
    $('tw-sub').textContent = `${applied} de ${tweaks.length} ya están aplicados en este PC.`;
    twButtons(); twAllCard();
  }
  // «Aplicar todo»: los de riesgo bajo que faltan. Fuera la impresión (rompe imprimir) y, en laptop, lo que gasta batería.
  let twLaptop = false;
  const twAll = () => tweaks.filter((t) => t.risk === 'bajo' && !t.blocked && t.state !== 'applied' && t.state !== 'na'
    && !['print-off', 'boot-info'].includes(t.id) && !(twLaptop && ['power-ultimate', 'hibernate-off'].includes(t.id)));
  function twAllCard() {
    const n = twAll().length;
    $('tw-all-t').textContent = n ? plural(n, 'ajuste por aplicar', 'ajustes por aplicar') : 'Todo aplicado';
    $('tw-all-p').textContent = n ? 'Con copia: se puede deshacer.' : '';
    $('tw-all').disabled = !n; $('tw-all').hidden = !n;
  }
  // Aplicar/Deshacer de arriba solo cuando se eligen uno por uno
  const twOne = document.querySelector('.tw-one'), twAct = $('tw-apply').parentElement;
  twAct.hidden = true; twOne.addEventListener('toggle', () => { twAct.hidden = !twOne.open; });
  $('tw-all').addEventListener('click', () => {
    const ids = twAll().map((t) => t.id); if (!ids.length) return;
    busyBtn($('tw-all'), true, 'Aplicando…'); $('tw-apply').disabled = $('tw-revert').disabled = true;
    send({ cmd: 'tweaksApply', ids });
  });
  $('tw-apply').addEventListener('click', () => { busyBtn($('tw-apply'), true, 'Aplicando…'); $('tw-revert').disabled = true; send({ cmd: 'tweaksApply', ids: twSel() }); });
  $('tw-revert').addEventListener('click', () => { busyBtn($('tw-revert'), true, 'Deshaciendo…'); $('tw-apply').disabled = true; send({ cmd: 'tweaksRevert', ids: twSel() }); });
  function renderTweaksDone(b) {
    busyBtn($('tw-apply'), false, 'Aplicar'); busyBtn($('tw-revert'), false, 'Deshacer'); busyBtn($('tw-all'), false, 'Aplicar todo');
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
    smApps();
    if (!$('view-apps').hidden) send({ cmd: 'tweaksList' });
    const shown = list.filter((a) => a.installed);
    $('ap-remove').disabled = true;
    $('ap-rec').hidden = $('ap-remove').hidden = !shown.length;
    if (!shown.length) {
      $('ap-list').replaceChildren(el('li', 'empty', 'No queda ninguna por quitar.'));
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
    const row = el('div', 'ap-note-act'); row.append(el('span', null, 'Windows puede reinstalar algunas por su cuenta.'), b);
    n.hidden = false; n.replaceChildren(row);
  }

  // ---------- fondo de VigíaOS ----------
  let wallBk = null;
  function renderWall(w) {
    busyBtn($('wall-set'), false, w.ours ? 'Ya lo tienes puesto' : 'Poner el fondo');
    $('wall-set').disabled = w.ours;
    $('wall-set').hidden = $('wall-sub').hidden = w.ours;   // ya puesto: solo queda la opción de volver
    wallBk = w.backupId;
    $('wall-back').hidden = !(w.ours && w.backupId);
  }
  $('wall-set').addEventListener('click', () => { busyBtn($('wall-set'), true, 'Poniendo…'); send({ cmd: 'wallSet' }); });
  $('wall-back').addEventListener('click', () => { if (!wallBk) return; busyBtn($('wall-back'), true, 'Volviendo…'); send({ cmd: 'backupRestore', id: wallBk }); });

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
  $('iso-dl-cancel').addEventListener('click', () => { busyBtn($('iso-dl-cancel'), true, 'Cancelando…'); send({ cmd: 'isoDlCancel' }); });
  function isoDl(d) {
    const box = $('iso-dl'), src = $('iso-src');
    if (d.state === 'busy') return;
    if (d.state === 'start') {
      box.hidden = false; $('iso-pick').disabled = $('iso-ms').disabled = true; busyBtn($('iso-dl-cancel'), false, 'Cancelar');
      $('iso-dl-step').textContent = 'Bajando Windows 11 de Microsoft'; $('iso-dl-bar').style.width = '0'; $('iso-dl-line').textContent = d.path;
      return;
    }
    if (d.state === 'progress') {
      $('iso-dl-bar').style.width = d.pct >= 0 ? `${d.pct}%` : '0';
      $('iso-dl-step').textContent = d.pct >= 0 ? `Bajando Windows 11 de Microsoft · ${d.pct} %` : 'Bajando Windows 11 de Microsoft';
      if (d.total > 0) $('iso-dl-line').textContent = `${gb(d.received)} de ${gb(d.total)}`;
      return;
    }
    box.hidden = true; $('iso-pick').disabled = $('iso-ms').disabled = false;
    if (d.state === 'error') { src.textContent = d.message; src.classList.add('bad'); }
  }
  $('iso-saveas').addEventListener('click', () => isoSrc && send({ cmd: 'isoSaveAs', source: isoSrc.path }));
  $('iso-cancel').addEventListener('click', () => { busyBtn($('iso-cancel'), true, 'Cancelando…'); send({ cmd: 'isoCancel' }); });
  function renderIsoSource(s) {
    if (s.busy) { $('iso-src').textContent = `Leyendo ${s.path}…`; $('iso-src').classList.remove('bad'); return; }
    if (s.error) { $('iso-src').textContent = `${s.path}: ${s.error}`; $('iso-src').classList.add('bad'); isoSrc = null; $('iso-opts').hidden = $('iso-go').hidden = true; return; }
    $('iso-src').classList.remove('bad');
    isoSrc = s; $('iso-src').textContent = `${s.path} · ${gb(s.bytes)}`;
    if (!usbIso) usbSetIso(s.path, s.bytes);
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
      noTelemetry: $('iso-tele').checked, noBitLocker: $('iso-bitlocker').checked,
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
    usbSetIso(d.output, d.bytes);
  }

  // ---------- pasar a un USB ----------
  let usbIso = null, usbDrives = [], usbBusy = false;
  function usbSetIso(path, bytes) { usbIso = path; $('usb-iso').textContent = `${path}${bytes ? ` · ${gb(bytes)}` : ''}`; usbReady(); }
  function usbSel() { return usbDrives.find((d) => String(d.number) === $('usb-drive').value) || null; }
  function usbReady() { $('usb-go').disabled = usbBusy || !usbIso || !usbSel(); }
  // pregunta antes de algo que no tiene vuelta atrás; resuelve true solo si se pulsa el botón rojo
  function ask(title, text, yes) {
    const d = $('ask'); $('ask-t').textContent = title; $('ask-p').textContent = text; $('ask-yes').textContent = yes;
    return new Promise((res) => {
      const done = (v) => { d.removeEventListener('close', onClose); if (d.open) d.close(); res(v); };
      const onClose = () => done(false);
      $('ask-yes').onclick = () => done(true); $('ask-no').onclick = () => done(false);
      d.addEventListener('close', onClose); d.showModal(); $('ask-no').focus();
    });
  }
  $('usb-refresh').addEventListener('click', () => { busyBtn($('usb-refresh'), true, 'Buscando…'); send({ cmd: 'usbList' }); });
  $('usb-drive').addEventListener('change', usbReady);
  $('usb-go').addEventListener('click', async () => {
    const d = usbSel(); if (!d || !usbIso) return;
    if (!await ask('¿Seguro?', `Se va a borrar todo lo que tiene ${d.name} (${gb(d.bytes)}). No se puede recuperar.`, 'Borrar y pasar')) return;
    usbBusy = true; usbReady();
    $('usb-result').textContent = ''; $('usb-result').classList.remove('bad');
    $('usb-run').hidden = false; $('usb-step').textContent = 'Empezando…'; $('usb-line').textContent = ''; $('usb-bar').style.width = '0';
    busyBtn($('usb-cancel'), false, 'Cancelar');
    send({ cmd: 'usbWrite', iso: usbIso, number: d.number, serial: d.serial });
  });
  $('usb-cancel').addEventListener('click', () => { busyBtn($('usb-cancel'), true, 'Cancelando…'); send({ cmd: 'usbCancel' }); });
  function usbProgress(p) {
    $('usb-step').textContent = `Paso ${p.num} de ${p.of}: ${p.step}`;
    const bar = $('usb-bar').parentElement;
    if (p.pct == null) { bar.classList.add('indet'); $('usb-line').textContent = ''; }
    else { bar.classList.remove('indet'); $('usb-bar').style.width = `${p.pct}%`; $('usb-line').textContent = `${Math.floor(p.pct)} %`; }
  }
  function usbDone(d) {
    usbBusy = false; $('usb-run').hidden = true;
    const r = $('usb-result'); r.classList.toggle('bad', !d.ok);
    r.textContent = d.ok ? `${d.message} Tardó ${Math.max(1, Math.round(d.minutes))} min.` : d.message;
    send({ cmd: 'usbList' });
  }

  // ---------- herramientas ----------
  function renderTools(p) {
    const mine = p.items.filter((t) => t.kind === 'folder');
    const apps = p.items.filter((t) => t.kind === 'app');
    $('tl-sub').textContent = `${mine.filter((t) => t.found).length} programas de tu carpeta, ${apps.length === 1 ? '1 programa instalado' : `${apps.length} programas instalados`} y ${p.items.length - mine.length - apps.length} herramientas de Windows.`;
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
    $('su-sub').textContent = `${plural(list.length, 'cosa arranca', 'cosas arrancan')} con Windows; ${on} ${on === 1 ? 'encendida' : 'encendidas'}.`;
    $('su-groups').replaceChildren(...suOrder.filter((g) => list.some((s) => s.group === g && !(s.missing && g === 'Programas'))).map((g) => {
      const sec = el('section', 'tw-group');
      sec.append(el('h2', null, g), el('p', 'sub su-note', suNote[g]));
      const ul = el('ul', 'rows');
      list.filter((s) => s.group === g && !(s.missing && g === 'Programas')).forEach((s) => {
        const li = el('li', `row su${s.enabled ? '' : ' is-off'}`);
        const body = el('div');
        const title = el('strong', null, s.name);
        body.append(title, el('p', null, [s.publisher, s.where].filter(Boolean).join(' · ')), Object.assign(el('p', 'src cmd', s.command), { title: s.command }));
        const gone = s.missing && g === 'Programas';   // ya se desinstaló: solo queda la entrada
        if (gone) body.append(el('p', 'src why', 'Ya no está instalado. Quitar borra esta entrada que quedó (con copia).'));
        else if (s.missing) body.append(el('p', 'src why', 'El programa ya no está en el disco: es seguro apagarlo.'));
        if (s.warning && !gone) body.append(el('p', 'src why', s.warning));
        const b = gone ? el('button', 'btn primary', 'Quitar') : el('button', `btn${s.enabled ? '' : ' primary'}`, s.enabled ? 'Apagar' : 'Encender');
        b.addEventListener('click', () => {
          if (gone) { busyBtn(b, true, 'Quitando…'); send({ cmd: 'startupRemove', id: s.id }); return; }
          busyBtn(b, true, s.enabled ? 'Apagando…' : 'Encendiendo…'); send({ cmd: 'startupSet', id: s.id, on: !s.enabled });
        });
        li.append(el('span', `state ${s.enabled && !gone ? 'applied' : ''}`, gone ? 'Desinstalado' : s.enabled ? 'Arranca' : 'Apagado'), body, b);
        ul.append(li);
      });
      sec.append(ul);
      return sec;
    }));
  }
  $('su-reload').addEventListener('click', () => { busyBtn($('su-reload'), true, 'Leyendo…'); send({ cmd: 'startupList' }); });
  // apagar todos los programas de una vez (Vigía y lo crítico se quedan); dos pasos
  let suArmed = 0;
  function suDisarm() { if (suArmed) clearTimeout(suArmed); suArmed = 0; const b = $('su-all'); b.textContent = 'Apagar todos los programas'; b.classList.remove('primary'); }
  function suAllShow(list) {
    const n = list.filter((s) => s.group === 'Programas' && s.enabled && !s.keep && !s.ours).length;
    $('su-all').hidden = $('su-all-note').hidden = n === 0;
    suDisarm();
  }
  $('su-all').addEventListener('click', () => {
    const b = $('su-all');
    if (!suArmed) { suArmed = setTimeout(suDisarm, 6000); b.textContent = '¿Seguro? Apagar todos'; b.classList.add('primary'); return; }
    suDisarm(); busyBtn(b, true, 'Apagando…'); send({ cmd: 'startupOffAll' });
  });
  function suAllDone(r) {
    busyBtn($('su-all'), false, 'Apagar todos los programas');
    const res = $('su-result'); res.classList.toggle('bad', r.failed.length > 0);
    const parts = [`${plural(r.off, 'programa apagado', 'programas apagados')}.`];
    if (r.kept) parts.push(`Se quedan encendidos: Vigía y ${r.keptNames.join(', ')}.`); else parts.push('Vigía sigue encendida.');
    if (r.failed.length) parts.push(`No se pudo con: ${r.failed.join('; ')}.`);
    res.textContent = parts.join(' ');
  }


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
  // ---------- Drift: el ratón o el stick se mueven solos (solo al pulsar Probar) ----------
  let drT = 0;
  $('dr-run').addEventListener('click', () => {
    const b = $('dr-run'); let n = 10;
    busyBtn(b, true, 'Midiendo…'); $('dr-list').hidden = true;
    $('dr-sub').textContent = `No toques nada… ${n}`;
    clearInterval(drT);
    drT = setInterval(() => { n = Math.max(0, n - 1); $('dr-sub').textContent = `No toques nada… ${n}`; if (!n) clearInterval(drT); }, 1000);
    send({ cmd: 'drift' });
  });
  function renderDrift(r) {
    clearInterval(drT); busyBtn($('dr-run'), false, 'Probar otra vez');
    const LV = { bad: 'bad', warn: 'warn', touched: 'info', ok: 'ok' };
    const bad = r.items.filter((i) => i.level === 'bad' || i.level === 'warn').length;
    $('dr-sub').textContent = !r.items.length ? 'No encontré ratón ni mando para medir.' : bad ? 'Algo se movió solo.' : r.items.some((i) => i.level === 'touched') ? 'Se tocó algo: vuelve a probar sin tocar nada.' : 'Nada se movió solo.';
    const order = { bad: 0, warn: 1, touched: 2, ok: 3 };
    $('dr-list').replaceChildren(...[...r.items].sort((a, b) => order[a.level] - order[b.level]).map((i) => {
      const li = el('li', `finding lv-${LV[i.level] || 'info'}`);
      const body = el('div'); body.append(el('p', null, i.title));
      if (i.detail) body.append(el('p', 'src', i.detail));
      if (i.fix) body.append(el('p', 'src', i.fix));
      li.append(el('span', 'dot'), el('strong', null, i.name), body);
      return li;
    }));
    $('dr-list').hidden = !r.items.length;
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
  const bkKind = { juegos: 'Juegos', ajustes: 'Ajustes', inicio: 'Inicio de Windows', pantalla: 'Pantalla', apps: 'Apps', fondo: 'Fondo de pantalla', restauracion: 'Restauración' };
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
    if (n.wallpaper) parts.push('el fondo de pantalla');
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

  // ---------- resolución estirada (en Juegos) ----------
  let dsTimer = null, dsNative = null, dsFortnite = false;
  const dsRes = (m) => `${m.width} × ${m.height}`;
  // Menos píxeles = menos trabajo para la tarjeta gráfica. Los FPS no suben en la misma proporción: en juegos
  // limitados por la gráfica suben más o menos entre la raíz y la potencia 0,7 de la reducción. Si limita el
  // procesador, casi nada. Es una estimación; lo real se ve midiendo una partida antes y otra después.
  function dsEstimate() {
    const v = $('ds-mode').value;
    const [w, h] = v === 'custom' ? [+$('ds-w').value, +$('ds-h').value] : v.split('x').map(Number);
    if (!dsNative || !w || !h) { $('ds-est').textContent = ''; return; }
    const ratio = (dsNative.width * dsNative.height) / (w * h);
    if (ratio <= 1.02) { $('ds-est').textContent = ''; return; }
    const less = Math.round((1 - 1 / ratio) * 100), lo = Math.round((ratio ** 0.5 - 1) * 100), hi = Math.round((ratio ** 0.7 - 1) * 100);
    $('ds-est').textContent = `+${lo}–${hi} % FPS`;
  }
  function renderDisplay(d) {
    const n = d.native, c = d.current;
    dsNative = d.saved || n;
    $('ds-restore').hidden = !d.saved;
    if (!n || !c) { $('ds-sub').textContent = 'No pude leer la pantalla principal.'; return; }
    $('ds-sub').textContent = c.hz > 1 ? (d.stretched ? `Ahora: ${dsRes(c)} estirada a ${c.hz} Hz. La normal es ${dsRes(d.saved)}.` : `Ahora: ${dsRes(c)} a ${c.hz} Hz.`)
      : d.stretched ? `Ahora: ${dsRes(c)} estirada. La normal es ${dsRes(d.saved)}.` : `Ahora: ${dsRes(c)}.`;
    // primero las que la tarjeta ya tiene; luego las de la lista de Vigía, que se crean en su panel al aplicarlas
    const extra = d.extra || [];
    const opt = (m, made) => {
      const o = el('option', null, dsRes(m));
      o.value = `${m.width}x${m.height}`;
      return o;
    };
    const opts = [...d.options.map((m) => opt(m, false)), ...extra.map((m) => opt(m, true))];
    const all = [...d.options, ...extra];
    const other = el('option', null, 'Otra…'); other.value = 'custom';
    $('ds-mode').replaceChildren(...opts, other);
    const cur = `${c.width}x${c.height}`;
    $('ds-mode').value = d.stretched && all.some((m) => `${m.width}x${m.height}` === cur) ? cur
      : all.some((m) => m.width === 1440 && m.height === 1080) ? '1440x1080' : all[0] ? `${all[0].width}x${all[0].height}` : 'custom';
    $('ds-gpu').textContent = '';
    dsFortnite = !!d.fortnite;
    $('ds-w').max = dsNative.width; $('ds-h').max = dsNative.height;
    if (!$('ds-w').value) { $('ds-w').value = Math.round(dsNative.height * 4 / 3 / 2) * 2; $('ds-h').value = dsNative.height; }
    $('ds-custom').hidden = $('ds-mode').value !== 'custom';
    $('ds-apply').disabled = false;
    $('ds-apply').textContent = d.stretched ? 'Cambiar' : 'Estirar';
    dsEstimate();
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
  $('ds-mode').addEventListener('change', () => { $('ds-custom').hidden = $('ds-mode').value !== 'custom'; dsEstimate(); });
  $('ds-w').addEventListener('input', dsEstimate);
  $('ds-h').addEventListener('input', dsEstimate);
  $('ds-apply').addEventListener('click', () => {
    const v = $('ds-mode').value;
    const [w, h] = v === 'custom' ? [Math.round(+$('ds-w').value), Math.round(+$('ds-h').value)] : v.split('x').map(Number);
    if (!(w >= 640 && h >= 480) || (dsNative && (w > dsNative.width || h > dsNative.height))) {
      $('ds-result').textContent = dsNative ? `Pon un tamaño entre 640 × 480 y ${dsRes(dsNative)}.` : 'Pon un tamaño válido.';
      $('ds-result').classList.add('bad'); return;
    }
    $('ds-result').textContent = '';
    busyBtn($('ds-apply'), true, 'Cambiando…');
    send({ cmd: 'displayStretch', w, h, games: dsFortnite && $('ds-games').checked });
  });
  $('ds-restore').addEventListener('click', () => { dsConfirm(false); send({ cmd: 'displayRestore' }); });
  $('ds-undo').addEventListener('click', () => { dsConfirm(false); send({ cmd: 'displayRestore' }); });
  $('ds-keep').addEventListener('click', () => { dsConfirm(false); $('ds-result').textContent = 'Listo. «Volver a la normal» la deja como estaba.'; $('ds-result').classList.remove('bad'); });

  // Cada sección carga sus datos al abrirla. Si desde entonces se cambió algo (en otra sección, o fuera de Vigía
  // mientras la ventana estaba oculta), se vuelven a leer al volver a ella. Escanear discos o almacenamiento solo la primera vez.
  const loaded = new Map();
  let changes = 0;
  const changed = () => { changes += 1; };
  const REREAD = new Set(['opt', 'diag', 'sug', 'mem', 'tweaks', 'apps', 'simple', 'tools', 'startup', 'games', 'backups']);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') changed(); });
  document.querySelectorAll('.nav[data-view]').forEach((b) => b.addEventListener('click', () => {
    const v = b.dataset.view;
    if (loaded.has(v) && (loaded.get(v) === changes || !REREAD.has(v))) return;
    const again = loaded.has(v);
    loaded.set(v, changes);
    if (v === 'simple' && again && $('sm-ask').hidden) return;   // modo simple corriendo o terminado: no se toca
    if (v === 'health') $('h-scan').click();
    else if (v === 'opt') send({ cmd: 'optStatus', analyze: false });
    else if (v === 'diag') $('d-run').click();
    else if (v === 'sug') { if (!sugDone || again) $('s-run').click(); }
    else if (v === 'storage' && !targets.length) $('scan').click();
    else if (v === 'mem') send({ cmd: 'settingsGet' });
    else if (v === 'tweaks') send({ cmd: 'tweaksList' });
    else if (v === 'apps' || v === 'simple') { if (v === 'simple' && bloat.length) smApps(); else send({ cmd: 'bloatList' }); }
    else if (v === 'bios') send({ cmd: 'biosGet' });
    else if (v === 'iso' && !usbBusy) send({ cmd: 'usbList' });
    else if (v === 'tools') send({ cmd: 'toolsList' });
    else if (v === 'startup') send({ cmd: 'startupList' });
    else if (v === 'games') { send({ cmd: 'gamesGet' }); send({ cmd: 'displayStatus' }); }
    else if (v === 'backups') send({ cmd: 'backupsList' });
    else if (v === 'support') send({ cmd: 'supportGet' });
  }));
  // ---------- modo simple ----------
  const SM_Q = [
    { k: 'games', q: '¿Juegas en esta PC?', h: 'Prioridad para juegos, GPU potente, modo juego y ratón sin aceleración.', def: true },
    { k: 'printer', q: '¿Usas impresora o «Imprimir a PDF»?', h: 'Si dices que no, se apaga la cola de impresión.', def: true },
    { k: 'xbox', q: '¿Usas Xbox, Game Pass o la barra Win+G?', h: 'Si no, se quitan las apps y servicios de Xbox.', def: false },
    { k: 'search', q: '¿Buscas archivos desde el menú Inicio?', h: 'Si no, se apaga el indexador y deja de leer el disco por detrás.', def: true },
    { k: 'laptop', q: '¿Es una laptop que usas con batería?', h: 'Si es laptop, no se pone el plan Máximo rendimiento ni se apaga la hibernación.', def: false },
    { k: 'visual', q: '¿Quitar transparencias y animaciones?', h: 'Windows se ve más simple y responde más rápido.', def: true },
    { k: 'privacy', q: '¿Apagar telemetría y anuncios de Windows?', h: 'Menos datos a Microsoft y nada de apps sugeridas.', def: true },
    { k: 'clean', q: '¿Borrar archivos temporales?', h: 'Solo temporales de más de 24 horas. Lo que esté en uso se salta.', def: true },
    { k: 'memAuto', q: '¿Liberar la memoria sola al llegar al 60 %?', h: 'Vigía la libera sin que hagas nada.', def: true },
  ];
  const smAns = Object.fromEntries(SM_Q.map((x) => [x.k, x.def]));
  const smKeep = new Set();
  let smAppsDrawn = false, smTimer = 0, smStages = [];
  const smAnswers = () => ({ ...smAns, keepApps: [...smKeep] });
  const smAskPlan = () => { clearTimeout(smTimer); smTimer = setTimeout(() => send({ cmd: 'simplePlan', answers: smAnswers() }), 150); };
  (function smDraw() {
    const box = $('sm-qs');
    for (const x of SM_Q) {
      const d = document.createElement('div'); d.className = 'sm-q';
      const b = document.createElement('b'); b.textContent = x.q;
      const h = document.createElement('span'); h.textContent = x.h;
      const seg = document.createElement('div'); seg.className = 'seg'; seg.setAttribute('role', 'radiogroup'); seg.setAttribute('aria-label', x.q);
      for (const [label, val] of [['Sí', true], ['No', false]]) {
        const o = document.createElement('button'); o.type = 'button'; o.setAttribute('role', 'radio'); o.textContent = label;
        o.setAttribute('aria-checked', String(smAns[x.k] === val));
        o.addEventListener('click', () => { smAns[x.k] = val; seg.querySelectorAll('button').forEach((n) => n.setAttribute('aria-checked', String(n === o))); smAskPlan(); });
        seg.append(o);
      }
      d.append(b, h, seg); box.append(d);
    }
  })();
  const SM_XBOX = ['Microsoft.GamingApp', 'Microsoft.XboxGamingOverlay', 'Microsoft.XboxApp'];
  function smApps() {
    const list = bloat.filter((a) => a.installed && !a.blocked && !SM_XBOX.includes(a.id));
    const box = $('sm-applist'); box.textContent = '';
    $('view-simple').querySelector('.sm-apps').hidden = !list.length;   // sin apps de más: ni se muestra
    if (!list.length) { smAskPlan(); return; }
    if (!smAppsDrawn) { list.filter((a) => a.risk !== 'bajo').forEach((a) => smKeep.add(a.id)); smAppsDrawn = true; }
    for (const a of list) {
      const l = document.createElement('label'); l.className = 'chk';
      const c = document.createElement('input'); c.type = 'checkbox'; c.checked = smKeep.has(a.id);
      c.addEventListener('change', () => { if (c.checked) smKeep.add(a.id); else smKeep.delete(a.id); smAskPlan(); });
      const t = document.createElement('div'); const b = document.createElement('b'); b.textContent = a.name; t.append(b);
      l.append(c, t); box.append(l);
    }
    smAskPlan();
  }
  function smPlan(p) {
    const n = p.changes;
    $('sm-count').textContent = n ? `Se harán ${plural(n, 'cambio', 'cambios')}, con copia de seguridad antes.` : 'Ya está todo optimizado según tus respuestas. Igual puedes limpiar y liberar memoria.';
    $('sm-go').disabled = false;
    smStages = p.stages;
  }
  const SM_CHECK = '<svg viewBox="0 0 12 12"><path d="M2.5 6.2 5 8.6 9.6 3.6"/></svg>';
  const SM_HALF = '<svg viewBox="0 0 12 12"><path d="M6 2.6v4.2M6 9.2v.1"/></svg>';
  $('sm-go').addEventListener('click', () => {
    $('sm-go').disabled = true;
    $('sm-ask').hidden = true; $('sm-done').hidden = true; $('sm-run').hidden = false;
    $('sm-ring').classList.remove('done'); $('sm-title').textContent = 'Optimizando…'; $('sm-now').textContent = 'No apagues el PC. Puedes seguir usándolo.';
    smSetPct(0);
    const ol = $('sm-stages'); ol.textContent = '';
    for (const st of smStages) {
      const li = document.createElement('li'); li.dataset.id = st.id;
      const i = document.createElement('i'); i.innerHTML = SM_CHECK;
      const t = document.createElement('div'); const b = document.createElement('b'); b.textContent = st.title; const sm = document.createElement('small');
      const n = st.tweaks.length + st.apps.length; if (n) sm.textContent = plural(n, 'cambio', 'cambios');
      t.append(b, sm); li.append(i, t); ol.append(li);
    }
    send({ cmd: 'simpleRun', answers: smAnswers() });
  });
  let smPctNow = 0, smRaf = 0;
  function smSetPct(v) {
    $('sm-arc').style.strokeDashoffset = String(326.7 * (1 - v / 100));
    cancelAnimationFrame(smRaf);
    const from = smPctNow, t0 = performance.now();
    const tick = (t) => { const k = Math.min(1, (t - t0) / 600); smPctNow = Math.round(from + (v - from) * (1 - (1 - k) ** 3)); $('sm-pct').textContent = smPctNow; if (k < 1) smRaf = requestAnimationFrame(tick); };
    smRaf = requestAnimationFrame(tick);
  }
  function smStep(st) {
    const li = $('sm-stages').children[st.index]; if (!li) return;
    li.className = st.state;
    if (st.state === 'warn') li.querySelector('i').innerHTML = SM_HALF;
    if (st.detail) li.querySelector('small').textContent = st.detail;
    if (st.state === 'run') $('sm-now').textContent = `${li.querySelector('b').textContent}…`;
    const total = $('sm-stages').children.length;
    smSetPct(Math.round(((st.state === 'run' ? st.index + 0.3 : st.index + 1) / total) * 100));
  }
  function smDone(r) {
    smSetPct(100); $('sm-ring').classList.add('done');
    const half = r.failed || $('sm-stages').querySelector('li.warn');
    $('sm-title').textContent = half ? 'Listo, con avisos' : 'Listo';
    $('sm-now').textContent = r.needsReboot ? 'Reinicia cuando te convenga para que todo haga efecto.' : 'Todo hizo efecto ya.';
    const parts = [];
    if (r.applied) parts.push(plural(r.applied, 'ajuste puesto', 'ajustes puestos'));
    if (r.appsRemoved) parts.push(plural(r.appsRemoved, 'app quitada', 'apps quitadas'));
    if (r.cleanedMb >= 1) parts.push(r.cleanedMb >= 1024 ? `${(r.cleanedMb / 1024).toFixed(1)} GB de temporales borrados` : `${Math.round(r.cleanedMb)} MB de temporales borrados`);
    if (r.freedMb >= 1) parts.push(r.freedMb >= 1024 ? `${(r.freedMb / 1024).toFixed(1)} GB de memoria liberados` : `${Math.round(r.freedMb)} MB de memoria liberados`);
    $('sm-done-title').textContent = r.failed ? `${plural(r.failed, 'cosa no se pudo', 'cosas no se pudieron')} hacer` : 'Tu PC quedó optimizada';
    $('sm-done-line').textContent = (parts.length ? parts.join(' · ') + '. ' : '') + 'Si algo no te gusta, en Copias tienes «Volver a como estaba».';
    const ul = $('sm-problems'); ul.textContent = ''; ul.hidden = !r.problems.length;
    for (const p of r.problems.slice(0, 8)) { const li = document.createElement('li'); li.textContent = p; ul.append(li); }
    $('sm-done').hidden = false;
    changed();
  }
  $('sm-again').addEventListener('click', () => { $('sm-run').hidden = true; $('sm-done').hidden = true; $('sm-ask').hidden = false; send({ cmd: 'bloatList' }); });
  $('sm-backups').addEventListener('click', () => document.querySelector('.nav[data-view="backups"]').click());

  // ---------- lo que más consume ----------
  const prKind = { game: 'juego', windows: 'Windows', background: 'sin ventana' };
  function renderProcs(v) {
    const tb = $('pr-rows');
    if (!v.top.length) return;
    $('pr-sub').textContent = v.game ? `Jugando ${v.game}: el consumo alto ahí es normal, no se marca.` : '';
    const warn = v.top.filter((r) => r.level === 'warn');
    $('pr-alert').hidden = !warn.length;
    if (warn.length) $('pr-alert').textContent = warn.length === 1 ? `${warn[0].name} está consumiendo más de lo normal.` : `${warn.length} programas están consumiendo más de lo normal.`;
    tb.textContent = '';
    for (const r of v.top) {
      const tr = document.createElement('tr'); if (r.level === 'warn') tr.className = 'warn';
      const n = document.createElement('td'); const b = document.createElement('b'); b.textContent = r.name; n.append(b);
      if (r.count > 1) n.append(` ×${r.count}`);
      if (prKind[r.kind]) { const g = document.createElement('span'); g.className = 'pr-tag'; g.textContent = prKind[r.kind]; n.append(g); }
      if (r.why && (r.level === 'warn' || r.kind !== 'app')) { const w = document.createElement('small'); w.textContent = r.why; n.append(w); }
      const c = document.createElement('td'); c.className = 'num'; c.textContent = `${fmt(r.cpu, 1)} %`;
      const a = document.createElement('td'); a.className = 'num'; a.textContent = v.seconds >= 10 ? `${fmt(r.cpuAvg, 1)} %` : '…';
      const m = document.createElement('td'); m.className = 'num'; m.textContent = r.ramGb >= 1 ? `${fmt(r.ramGb, 1)} GB` : `${Math.round(r.ramGb * 1024)} MB`;
      const act = document.createElement('td'); act.className = 'pr-acts';
      if (r.path) { const o = document.createElement('button'); o.className = 'btn'; o.textContent = 'Ubicación'; o.title = r.path; o.addEventListener('click', () => send({ cmd: 'procReveal', exe: r.exe })); act.append(o); }
      if (r.canClose && r.level === 'warn') { const k = document.createElement('button'); k.className = 'btn'; k.textContent = 'Cerrar'; bkArm(k, 'Cerrar', '¿Seguro? Pulsa otra vez', () => { busyBtn(k, true, 'Cerrando…'); send({ cmd: 'procClose', exe: r.exe }); }); act.append(k); }
      tr.append(n, c, a, m, act); tb.append(tr);
    }
  }
  const prWatch = () => send({ cmd: 'procWatch', on: !$('view-monitor').hidden });
  document.querySelectorAll('.nav[data-view]').forEach((b) => b.addEventListener('click', prWatch));
  prWatch();

  // cualquier «Leyendo…», «Escaneando…» lleva el anillo de carga
  const markLoading = (root) => root.querySelectorAll?.('.empty').forEach((e) => e.classList.toggle('loading', /…\s*$/.test(e.textContent)));
  markLoading(document);
  new MutationObserver((ms) => { for (const m of ms) for (const n of m.addedNodes) if (n.nodeType === 1) { if (n.classList.contains('empty')) markLoading(n.parentNode); else markLoading(n); } })
    .observe(document.body, { childList: true, subtree: true });

  // para --snap: abrir una sección
  // 'opt-run' = abrir Optimizar y lanzar la primera unidad (prueba de extremo a extremo)
  window.vigiaSnap = (spec) => {
    const [v, lang, dbg] = spec.split('@');   // 'settings@en' = Configuración en inglés; 'settings@en@dbg' añade el diagnóstico
    if (lang) window.vigiaI18n.set(lang);
    if (v === 'ask') { document.querySelector('.nav[data-view="iso"]')?.click(); setTimeout(() => ask('¿Seguro?', `Se va a borrar todo lo que tiene ${'Kingston DataTraveler 3.0'} (${gb(15472000000)}). No se puede recuperar.`, 'Borrar y pasar'), 1500); return; }
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
    // 'simple-demo' / 'simple-done': solo para capturas; pinta el progreso sin mandar nada al backend
    if (view === 'simple' && (act === 'demo' || act === 'done')) setTimeout(() => {
      $('sm-ask').hidden = true; $('sm-run').hidden = false;
      const ol = $('sm-stages'); ol.textContent = '';
      for (const st of smStages) { const li = document.createElement('li'); const i = document.createElement('i'); i.innerHTML = SM_CHECK; const t = document.createElement('div'); const b = document.createElement('b'); b.textContent = st.title; t.append(b, document.createElement('small')); li.append(i, t); ol.append(li); }
      const upto = act === 'done' ? smStages.length : 4;
      for (let k = 0; k < upto; k++) smStep({ index: k, state: k === 2 ? 'warn' : 'ok', detail: k === 2 ? '5 de 6 listos.' : 'Hecho.' });
      if (act === 'demo') smStep({ index: 4, state: 'run' });
      else smDone({ failed: 0, applied: 18, appsRemoved: 3, cleanedMb: 1450, freedMb: 2200, needsReboot: true, problems: [] });
    }, 2500);
  };

  // ---------- actualizaciones (las instala Puente) ----------
  let upAsked = false, upBusy = false, upInfo = null;
  function renderUpdate(u) {
    upInfo = u; upBusy = false;
    $('st-ver').textContent = `Tienes la versión ${u.current}`;
    busyBtn($('st-check'), false, 'Buscar actualizaciones');
    busyBtn($('st-run'), false, 'Actualizar ahora');
    if (u.available) {
      $('st-new').hidden = false;
      $('st-new-title').textContent = `Versión nueva: ${u.latest}`;
      $('st-notes').textContent = (u.notes || '').trim();
      $('st-state').textContent = u.error || 'Se cierra un momento y vuelve a abrir ya actualizada.';
      $('st-state').classList.toggle('bad', !!u.error);
    } else {
      $('st-new').hidden = true;
      if (upAsked) {
        $('st-state').textContent = u.error ? `No pude mirar si hay versión nueva: ${u.error}` : `Estás en la última versión (${u.current}).`;
        $('st-state').classList.toggle('bad', !!u.error);
      }
    }
    upAsked = false;
    renderNews();
  }
  $('st-check').addEventListener('click', () => { upAsked = true; busyBtn($('st-check'), true, 'Buscando…'); send({ cmd: 'updateCheck' }); });
  const runUpdate = () => { upBusy = true; busyBtn($('st-run'), true, 'Preparando…'); renderNews(); send({ cmd: 'updateRun' }); };
  $('st-run').addEventListener('click', runUpdate);

  // ---------- novedades ----------
  // Cada versión es una fila, como un chat: las que no has leído van en negrita con su contador. Al abrirla
  // se marca como leída y muestra sus cambios con la etiqueta Nuevo, Mejora o Arreglo. El historial viene con
  // la app (changelog.json); si hay una versión nueva, va arriba con sus notas y el botón para actualizar.
  const NEWS_TAG = { nuevo: 'Nuevo', mejora: 'Mejora', arreglo: 'Arreglo' };
  let newsList = [], newsOpen = new Set(), newsRead = null;
  const vnum = (v) => String(v || '0').split('.').map(Number);
  const newer = (a, b) => { const x = vnum(a), y = vnum(b); for (let k = 0; k < 3; k++) if ((x[k] || 0) !== (y[k] || 0)) return (x[k] || 0) > (y[k] || 0); return false; };
  const curVer = () => upInfo?.current || '';
  function parseNotes(md) {
    let lines = String(md || '').split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    const bullets = lines.filter((l) => /^[-*] /.test(l));
    lines = bullets.length ? bullets : lines.filter((l) => !/:$/.test(l)).flatMap((l) => l.split(/(?<=\.)\s+(?=[A-ZÁÉÍÓÚÑ«])/));
    return lines.map((l) => {
      let t = l.replace(/^[-*]\s+/, '').replace(/\*\*/g, '').replace(/\[([^\]]+)\]\([^)]+\)/g, '$1');
      let type;
      const m = t.match(/^(nuevo|nueva|mejora|arreglo|arreglado|fix|new)\s*[:·-]\s*/i);
      if (m) { type = /^mejora/i.test(m[1]) ? 'mejora' : /^(arregl|fix)/i.test(m[1]) ? 'arreglo' : 'nuevo'; t = t.slice(m[0].length); }
      else type = /ya no|arregl|corrig|fallaba|no avisaba/i.test(t) ? 'arreglo' : /^(nuev|ahora puedes)/i.test(t) ? 'nuevo' : 'mejora';
      return { type, text: t.charAt(0).toUpperCase() + t.slice(1) };
    }).filter((i) => i.text).slice(0, 12);
  }
  function newsAll() {
    const cur = curVer();
    let list = cur ? newsList.filter((e) => !newer(e.version, cur)) : newsList.slice();
    if (upInfo?.available && upInfo.latest && !list.some((e) => e.version === upInfo.latest))
      list = [{ version: upInfo.latest, date: '', title: 'Versión nueva disponible', items: parseNotes(upInfo.notes), available: true }, ...list];
    return list;
  }
  function loadRead(list) {
    if (newsRead) return;
    try { newsRead = JSON.parse(localStorage.getItem('vigia-news-read') || 'null'); } catch { newsRead = null; }
    if (!Array.isArray(newsRead)) {   // primera vez: lo viejo cuenta como leído; solo la versión instalada queda nueva
      newsRead = list.filter((e) => e.version !== curVer() && !e.available).map((e) => e.version);
      saveRead();
    }
  }
  function saveRead() { try { localStorage.setItem('vigia-news-read', JSON.stringify(newsRead.slice(-40))); } catch { /* sin almacenamiento */ } }
  function newsWhen(e) {
    if (e.available) return 'Disponible';
    if (e.version === curVer()) return 'Instalada';
    if (!e.date) return '';
    const [, m, d] = e.date.split('-').map(Number);   // a mano: así no depende de los idiomas que traiga WebView2
    const MON = { es: 'ene feb mar abr may jun jul ago sept oct nov dic', en: 'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec', pt: 'jan fev mar abr mai jun jul ago set out nov dez', fr: 'janv févr mars avr mai juin juil août sept oct nov déc', de: 'Jan. Feb. März Apr. Mai Juni Juli Aug. Sept. Okt. Nov. Dez.' };
    const lg = window.vigiaI18n.lang, mm = (MON[lg] || MON.es).split(' ')[m - 1];
    return lg === 'en' ? `${mm} ${d}` : lg === 'de' ? `${d}. ${mm}` : `${d} ${mm}`;
  }
  function renderNews() {
    if (!curVer()) return;
    const list = newsAll(); loadRead(list);
    const unread = (e) => !newsRead.includes(e.version);
    const n = list.filter(unread).length;
    $('news-count').hidden = !n; $('news-count').textContent = n > 9 ? '9+' : String(n);
    const avail = list.find((e) => e.available);
    $('news-btn').classList.toggle('has-update', !!avail);
    $('news-sub').textContent = avail ? `Versión ${avail.version} lista` : `Versión ${curVer()}`;
    $('news-upd').hidden = !avail;
    if (avail) {
      $('news-upd-t').textContent = `Versión ${avail.version} disponible`;
      $('news-upd-s').textContent = upInfo?.error ? upInfo.error : upBusy ? 'Se cierra un momento y vuelve a abrir ya actualizada.' : (upInfo?.size ? `Tienes la ${curVer()} · ${Math.round(upInfo.size / 1048576)} MB` : `Tienes la ${curVer()}`);
      $('news-upd-s').classList.toggle('bad', !!upInfo?.error && !upBusy);
      busyBtn($('news-upd-btn'), upBusy, upBusy ? 'Preparando…' : 'Actualizar');
      $('news-upd-bar').hidden = !upBusy;
    }
    const L = $('news-list');
    if (!list.length) { L.replaceChildren(el('li', 'news-empty', 'Sin novedades por ahora')); return; }
    L.replaceChildren(...list.map((e) => {
      const open = newsOpen.has(e.version), un = unread(e);
      const li = el('li', 'news-row' + (un ? ' unread' : '') + (open ? ' open' : '')); li.dataset.v = e.version;
      const b = el('button', 'news-row-btn'); b.setAttribute('aria-expanded', String(open));
      const main = el('span', 'news-main');
      main.append(el('span', 't', e.title || `Versión ${e.version}`), el('span', 's', open ? `Versión ${e.version}` : (e.items?.[0]?.text || '')));
      const meta = el('span', 'news-meta'); meta.append(el('span', 'when', newsWhen(e)));
      if (un) meta.append(el('span', 'news-count', String(e.items?.length || 1)));
      const av = el('span', 'news-av', e.version.split('.').slice(0, 2).join('.')); av.translate = false;
      b.append(av, main, meta); li.append(b);
      if (open) {
        const ul = el('ul', 'news-items');
        (e.items || []).forEach((it) => { const r = el('li'); r.append(el('span', 'news-tag ' + (NEWS_TAG[it.type] ? it.type : 'mejora'), NEWS_TAG[it.type] || 'Mejora'), el('span', null, it.text)); ul.append(r); });
        li.append(ul);
      }
      return li;
    }));
  }
  function markRead(v) { if (!v || !newsRead || newsRead.includes(v)) return; newsRead.push(v); saveRead(); }
  function setNewsOpen(on) {
    const pop = $('news-pop'); pop.hidden = !on; $('news-btn').setAttribute('aria-expanded', String(on));
    if (!on) return;
    if (!newsOpen.size) { const f = newsAll()[0]; if (f) newsOpen.add(f.version); }
    pop.style.left = Math.round(document.querySelector('.rail').getBoundingClientRect().right + 8) + 'px';
    newsOpen.forEach(markRead); renderNews(); pop.focus();
  }
  $('news-btn').addEventListener('click', (e) => { e.stopPropagation(); setNewsOpen($('news-pop').hidden); });
  $('news-close').addEventListener('click', () => setNewsOpen(false));
  $('news-list').addEventListener('click', (e) => {
    const row = e.target.closest('.news-row'); if (!row) return;
    const v = row.dataset.v;
    if (newsOpen.has(v)) newsOpen.delete(v); else newsOpen.add(v);
    markRead(v); renderNews();
  });
  $('news-upd-btn').addEventListener('click', runUpdate);
  document.addEventListener('mousedown', (e) => { const pop = $('news-pop'); if (!pop.hidden && !pop.contains(e.target) && !e.target.closest('#news-btn')) setNewsOpen(false); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('news-pop').hidden) setNewsOpen(false); });
  fetch('changelog.json').then((r) => r.json()).then((l) => { newsList = Array.isArray(l) ? l : []; renderNews(); }).catch(() => { /* sin historial */ });
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
  $('st-boot').addEventListener('change', () => { $('st-boot').disabled = true; $('st-boot-msg').textContent = ''; send({ cmd: 'autoStartSet', on: $('st-boot').checked }); });
  function bootDone(d) {
    const b = $('st-boot'); b.disabled = false; b.checked = d.on;
    const m = $('st-boot-msg'); m.classList.toggle('bad', !d.ok);
    m.textContent = !d.ok ? `No se pudo: ${d.message}` : d.on ? 'Listo: la próxima vez que entres a Windows, Vigía se queda en la bandeja.' : 'Listo: Vigía ya no se abre sola.';
  }
  $('st-auto').addEventListener('change', () => { if (cfg) { cfg.updateCheck = $('st-auto').checked; send({ cmd: 'settingsSet', cfg }); } });
  send({ cmd: 'settingsGet' });

  // ---------- ayuda y apoyo ----------
  const goView = (v) => document.querySelector(`.nav[data-view="${v}"]`)?.click();
  document.querySelectorAll('.hp-go').forEach((b) => b.addEventListener('click', () => goView(b.dataset.go)));
  const DOWNLOAD = 'https://jcperezdev.github.io/vigia-releases/';
  $('sp-star').addEventListener('click', () => send({ cmd: 'openUrl', url: 'repo' }));
  $('sp-issue').addEventListener('click', () => send({ cmd: 'openUrl', url: 'https://github.com/JcperezDev/vigia-releases/issues/new?title=Falla%3A%20' }));
  $('sp-idea').addEventListener('click', () => send({ cmd: 'openUrl', url: 'https://github.com/JcperezDev/vigia-releases/issues/new?title=Idea%3A%20' }));
  $('sp-share').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(DOWNLOAD); $('sp-result').textContent = 'Enlace copiado.'; }
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
    box.append(el('p', 'sp-safe', 'Se abre en el navegador. Vigía no ve el pago.'));
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
    { view: 'games', at: '#ds-box', title: 'Resolución estirada',
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
  // respuestas que significan que algo del sistema cambió
  const DONE = new Set(['tweaksDone', 'simpleDone', 'bloatDone', 'startupAll', 'secDone', 'rigDone', 'gameDone', 'backupDone', 'optDone', 'autoStart', 'wallDone', 'memFreed', 'startupDone', 'displayDone', 'toolDone']);
  if (host) host.addEventListener('message', (e) => {
    const { type, payload } = e.data;
    if (DONE.has(type)) changed();
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
      if (typeof payload.autoStart === 'boolean') $('st-boot').checked = payload.autoStart;
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
    else if (type === 'usbList') usbList(payload);
    else if (type === 'usbIso') usbSetIso(payload.path, payload.bytes);
    else if (type === 'usbProgress') usbProgress(payload);
    else if (type === 'usbDone') usbDone(payload);
    else if (type === 'isoDl') isoDl(payload);
    else if (type === 'tweaksDone') renderTweaksDone(payload);
    else if (type === 'bloat') { renderBloat(payload); send({ cmd: 'wallGet' }); }
    else if (type === 'wall') renderWall(payload);
    else if (type === 'wallDone') { $('wall-result').textContent = payload.message; $('wall-result').classList.toggle('bad', !payload.ok); }
    else if (type === 'simplePlan') smPlan(payload);
    else if (type === 'simpleStep') smStep(payload);
    else if (type === 'simpleDone') smDone(payload);
    else if (type === 'procs') renderProcs(payload);
    else if (type === 'procDone') { $('pr-alert').hidden = false; $('pr-alert').textContent = payload.message; }
    else if (type === 'bloatDone') {
      busyBtn($('ap-remove'), false, 'Quitar seleccionadas');
      const bad = payload.filter((r) => !r.ok);
      $('ap-result').textContent = bad.length ? `No se pudieron quitar: ${bad.map((r) => `${r.id} (${r.error})`).join('; ')}.` : `${plural(payload.length, 'app quitada', 'apps quitadas')}.`;
    }
    else if (type === 'tools') renderTools(payload);
    else if (type === 'startup') { renderStartup(payload); suAllShow(payload); }
    else if (type === 'startupAll') suAllDone(payload);
    else if (type === 'autoStart') bootDone(payload);
    else if (type === 'games') { busyBtn($('gm-reload'), false, 'Recargar'); renderRig(payload.rig); renderFps(payload.fps); renderFortnite(payload.fortnite); }
    else if (type === 'rigDone') rigDone(payload);
    else if (type === 'gameDone') $('gm-result').textContent = payload.message;
    else if (type === 'fpsState') renderFpsState(payload);
    else if (type === 'drift') renderDrift(payload);
    else if (type === 'backups') renderBackups(payload);
    else if (type === 'backupDone') {
      $('bk-result').textContent = payload.message + (payload.errors.length ? ` ${payload.errors.join('; ')}` : '');
      $('bk-result').classList.toggle('bad', !payload.ok);
      if (payload.id === wallBk) {
        busyBtn($('wall-back'), false, 'Volver al fondo de antes');
        $('wall-result').textContent = payload.ok ? 'Listo, tienes otra vez tu fondo de antes.' : payload.message + (payload.errors.length ? ` ${payload.errors.join('; ')}` : '');
        $('wall-result').classList.toggle('bad', !payload.ok);
      }
      send({ cmd: 'wallGet' });
    }
    else if (type === 'startupDone') {
      $('su-result').textContent = payload.ok ? '' : `No se pudo cambiar: ${payload.message}`;
      $('su-result').classList.toggle('bad', !payload.ok);
    }
    else if (type === 'display') { busyBtn($('ds-apply'), false); renderDisplay(payload); }
    else if (type === 'displayDone') {
      // al estirar se enseña lo que hizo aparte del cambio (resolución creada en la gráfica, Fortnite)
      const extraMsg = payload.ok && payload.pending ? payload.message.replace(/^Pantalla en [^.]+\. ?/, '') : payload.message;
      $('ds-result').textContent = extraMsg;
      $('ds-result').classList.toggle('bad', !payload.ok);
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
