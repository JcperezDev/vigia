// Vigía · páginas de privacidad y aviso legal: muestra el idioma elegido en la portada o el del navegador.
(() => {
  'use strict';
  const LANGS = ['es', 'en', 'pt', 'fr', 'de'];
  const FOOT = {"es": ["Código fuente", "Privacidad", "Aviso legal"], "en": ["Source code", "Privacy", "Legal notice"], "pt": ["Código-fonte", "Privacidade", "Aviso legal"], "fr": ["Code source", "Confidentialité", "Mentions légales"], "de": ["Quellcode", "Datenschutz", "Rechtliches"]};
  function pick() {
    try { const s = localStorage.getItem('vigia-lang'); if (LANGS.includes(s)) return s; } catch { /* modo privado */ }
    for (const l of navigator.languages || [navigator.language || 'es']) {
      const p = String(l).slice(0, 2).toLowerCase();
      if (LANGS.includes(p)) return p;
    }
    return 'en';
  }
  const sel = document.getElementById('lang');
  function show(lang) {
    document.documentElement.lang = lang;
    document.querySelectorAll('article.doc').forEach((a) => {
      a.hidden = a.lang !== lang;
      if (!a.hidden) document.title = a.dataset.title;
    });
    document.querySelectorAll('footer [data-f]').forEach((a) => { a.textContent = FOOT[lang][a.dataset.f - 1]; });
    sel.value = lang;
  }
  sel.addEventListener('change', () => {
    try { localStorage.setItem('vigia-lang', sel.value); } catch { /* sin almacenamiento */ }
    show(sel.value);
  });
  show(pick());
})();
