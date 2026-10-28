// Idiomas de Vigía. El texto original está en español (en index.html, app.js y lo que manda el backend); este archivo
// lo traduce en pantalla con los diccionarios de i18n/<idioma>.json. Las claves con {0}, {1}… cubren textos con partes
// variables: las partes se traducen también (p. ej. «3 copias guardadas»). Lo que no está en el diccionario se queda en español.
(() => {
  'use strict';
  const LANGS = { es: 'Español', en: 'English', pt: 'Português', fr: 'Français', de: 'Deutsch' };
  const LOCALE = { es: 'es', en: 'en-US', pt: 'pt-BR', fr: 'fr', de: 'de' };
  let lang = 'es', dict = null, pats = [], cache = new Map();
  const orig = new WeakMap();      // nodo de texto -> { es, shown }
  const origAttr = new WeakMap();  // elemento -> { attr: { es, shown } }
  const ATTRS = ['title', 'aria-label', 'placeholder'];

  function load(code) {
    try {
      const x = new XMLHttpRequest();
      x.open('GET', `i18n/${code}.json`, false);   // síncrono: así no se ve el español un instante
      x.send();
      return x.status === 200 || x.status === 0 ? JSON.parse(x.responseText) : null;
    } catch { return null; }
  }

  function compile(d) {
    pats = Object.keys(d).filter((k) => /\{\d+\}/.test(k)).map((k) => {
      const order = [];
      const src = k.split(/(\{\d+\})/).map((p) => {
        const m = /^\{(\d+)\}$/.exec(p);
        if (m) { order.push(Number(m[1])); return '([\\s\\S]+?)'; }
        return p.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      }).join('');
      return { re: new RegExp(`^${src}$`), to: d[k], order, fixed: k.replace(/\{\d+\}/g, '').length };
    }).sort((a, b) => b.fixed - a.fixed);   // el patrón con más texto fijo gana
  }

  // Traduce un texto ya normalizado (sin espacios al borde)
  function tr(s, depth = 0) {
    if (!dict || !s || depth > 3 || !/[A-Za-zÀ-ÿ]/.test(s)) return s;
    if (Object.prototype.hasOwnProperty.call(dict, s)) return dict[s];
    if (depth === 0 && cache.has(s)) return cache.get(s);
    let res = s;
    // «12 apps quitadas», «3 copias guardadas»: número + palabra
    const num = /^([\d.,\s]+%?)\s+(.+)$/.exec(s);
    if (num && Object.prototype.hasOwnProperty.call(dict, num[2])) res = `${num[1]} ${dict[num[2]]}`;
    else {
      for (const p of pats) {
        const m = p.re.exec(s);
        if (!m) continue;
        const parts = p.order.map((_, i) => m[i + 1]);
        const done = parts.map((x) => tr(x.trim(), depth + 1));
        // una parte larga que no se pudo traducir = el patrón agarró una frase que no es la suya
        // (las fechas y cifras no se traducen y pueden ser largas: «Oct 27, 10:17 PM»)
        if (parts.some((x, i) => done[i] === x.trim() && x.trim().split(' ').length > 3 && !/\d/.test(x))) continue;
        const byIdx = {};
        p.order.forEach((n, i) => { byIdx[n] = done[i] === parts[i].trim() ? parts[i] : done[i]; });
        res = p.to.replace(/\{(\d+)\}/g, (_, n) => byIdx[n] ?? '');
        break;
      }
      // frases sueltas unidas con «. »: se traduce cada una
      if (res === s && /[.!?] [A-ZÁÉÍÓÚ¿]/.test(s) && depth < 2) {
        const bits = s.split(/(?<=[.!?]) (?=[A-ZÁÉÍÓÚ¿])/);
        const t = bits.map((b) => tr(b, depth + 1));
        if (t.some((x, i) => x !== bits[i])) res = t.join(' ');
      }
    }
    if (depth === 0) { if (cache.size > 4000) cache.clear(); cache.set(s, res); }
    return res;
  }

  function trRaw(raw) {
    const lead = /^\s*/.exec(raw)[0], tail = /\s*$/.exec(raw)[0];
    const core = raw.trim().replace(/\s+/g, ' ');
    const t = tr(core);
    return t === core ? raw : lead + t + tail;
  }

  function doText(n) {
    if (n.parentElement?.closest('[translate="no"]')) return;
    const v = n.nodeValue;
    const o = orig.get(n);
    if (o && v === o.shown) return;                // lo escribimos nosotros
    const es = v;
    const shown = lang === 'es' ? es : trRaw(es);
    orig.set(n, { es, shown });
    if (shown !== v) n.nodeValue = shown;
  }

  function doAttrs(e) {
    let o = origAttr.get(e);
    for (const a of ATTRS) {
      if (!e.hasAttribute(a)) continue;
      const v = e.getAttribute(a);
      const rec = o?.[a];
      if (rec && v === rec.shown) continue;
      const shown = lang === 'es' ? v : trRaw(v);
      if (!o) { o = {}; origAttr.set(e, o); }
      o[a] = { es: v, shown };
      if (shown !== v) e.setAttribute(a, shown);
    }
  }

  function walk(root) {
    if (root.nodeType === 3) { doText(root); return; }
    if (root.nodeType !== 1 || root.tagName === 'SCRIPT' || root.tagName === 'STYLE' || root.closest('[translate="no"]')) return;
    doAttrs(root);
    const it = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, {
      acceptNode: (n) => (n.nodeType === 1 && (n.tagName === 'SCRIPT' || n.tagName === 'STYLE' || n.getAttribute('translate') === 'no')) ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
    });
    for (let n = it.nextNode(); n; n = it.nextNode()) {
      try { n.nodeType === 3 ? doText(n) : doAttrs(n); }
      catch (e) { if (errors.length < 20) errors.push(`${e.message} @ ${(n.nodeValue || n.tagName || '').slice(0, 60)}`); }
    }
  }
  const errors = [];

  // Volver al español: cada nodo recuerda su texto original
  function restoreAll() {
    if (!document.body) return;
    const it = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
    for (let n = it.nextNode(); n; n = it.nextNode()) {
      // se olvida lo anotado: el siguiente idioma parte del español de nuevo
      if (n.nodeType === 3) { const o = orig.get(n); if (o) { if (n.nodeValue === o.shown) n.nodeValue = o.es; orig.delete(n); } }
      else { const o = origAttr.get(n); if (o) { for (const a in o) if (n.getAttribute(a) === o[a].shown) n.setAttribute(a, o[a].es); origAttr.delete(n); } }
    }
  }

  const obs = new MutationObserver((list) => {
    if (lang === 'es') return;
    for (const m of list) {
      if (m.type === 'characterData') doText(m.target);
      else if (m.type === 'attributes') doAttrs(m.target);
      else m.addedNodes.forEach(walk);
    }
  });

  function set(code) {
    code = LANGS[code] ? code : 'es';
    if (code === lang && (code === 'es' || dict)) return;
    restoreAll();
    lang = code; cache = new Map();
    dict = code === 'es' ? null : load(code);
    if (dict) compile(dict); else { pats = []; if (code !== 'es') lang = 'es'; }
    document.documentElement.lang = lang;
    if (lang !== 'es' && document.body) walk(document.body);
    window.dispatchEvent(new Event('vigia-lang'));
  }

  // «auto» = el idioma de Windows (lo que diga el navegador de la app), si lo tenemos; si no, inglés
  function resolve(pref) {
    if (pref && pref !== 'auto') return LANGS[pref] ? pref : 'es';
    const sys = (navigator.language || 'es').slice(0, 2).toLowerCase();
    return LANGS[sys] ? sys : 'en';
  }

  window.vigiaI18n = {
    langs: LANGS,
    get lang() { return lang; },
    locale: () => LOCALE[lang] || 'es',
    t: (s) => (lang === 'es' ? s : trRaw(s)),
    set: (pref) => set(resolve(pref)),
    resolve,
    errors,
    debug: (n) => JSON.stringify(orig.get(n) || null),
  };

  const start = () => {
    obs.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ATTRS });
    let pref = 'auto';
    try { pref = localStorage.getItem('vigia-lang') || 'auto'; } catch { /* sin almacenamiento */ }
    set(resolve(pref));
  };
  if (document.body) start(); else document.addEventListener('DOMContentLoaded', start);
})();
