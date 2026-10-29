// Saca todos los textos en español de la interfaz (index.html, app.js), del backend (*.cs) y del catálogo (tweaks.json)
// y los deja en app/Vigia/wwwroot/i18n/strings.json. Las partes variables (${x} en JS, {x} en C#) quedan como {0}, {1}…
// Uso: node tools/i18n-extract.js
'use strict';
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..', 'app', 'Vigia');
const out = new Map();   // clave -> fuentes

function add(s, src) {
  s = s.replace(/\s+/g, ' ').trim();
  if (!keep(s)) return;
  if (!out.has(s)) out.set(s, new Set());
  out.get(s).add(src);
}

// ¿Es texto para personas? (no rutas, órdenes, ids ni selectores)
function keep(s) {
  const letters = s.replace(/\{\d+\}/g, '');
  if (!/[A-Za-zÁÉÍÓÚáéíóúÑñü]{2,}/.test(letters)) return false;
  const words = s.split(' ').filter((w) => /[a-záéíóúñ]{3,}/i.test(w)).length;
  const prose = words >= 3;   // una frase: aunque nombre un archivo o una ruta, se traduce
  if (!prose && /\\|^[-./#$@]|^https?:|\.(exe|dll|json|ps1|sys|cmd|png|js|css|html)\b|^[a-z0-9]+[-_:][a-z0-9-_:]*$/i.test(s)) return false;
  if (/<\/?[a-z]/i.test(s)) return false;   // HTML armado en código
  if (/^[a-z][a-zA-Z0-9]*$/.test(s)) return false;             // ids en minúscula, nombres de comandos
  if (/^[A-Z][A-Za-z0-9]*$/.test(s) && !/[a-z]/.test(s.slice(1))) return false; // CPU, GPU, NVMe…
  if (/^[A-Z][a-zA-Z0-9]+$/.test(s) && /[A-Z].*[A-Z]/.test(s)) return false;   // CamelCase
  if (/^(SELECT|Get-|Set-|HK(LM|CU)|root|Win32_)/.test(s)) return false;
  if (/^[\x00-\x7F]*$/.test(s) && !/ /.test(s) && !/^[A-ZÁÉÍÓÚ][a-záéíóúñ]+$/.test(s)) return false;
  return true;
}

// ---------- JS: comillas, plantillas con ${…} anidadas ----------
function scanJs(code, src) {
  let i = 0;
  function str(q) {             // i en la comilla de apertura
    let s = ''; i++;
    while (i < code.length && code[i] !== q) { if (code[i] === '\\') { s += code[i + 1] === 'n' ? '\n' : code[i + 1]; i += 2; } else s += code[i++]; }
    i++; return s;
  }
  function tpl() {              // devuelve la plantilla con {n}
    let s = '', n = 0; i++;
    while (i < code.length && code[i] !== '`') {
      if (code[i] === '\\') { s += code[i + 1]; i += 2; }
      else if (code[i] === '$' && code[i + 1] === '{') { i += 2; expr(); s += `{${n++}}`; }
      else s += code[i++];
    }
    i++; return s;
  }
  function expr() {             // hasta la } que cierra
    let depth = 1;
    while (i < code.length) {
      const c = code[i];
      if (c === "'" || c === '"') add(str(c), src);
      else if (c === '`') add(tpl(), src);
      else if (c === '{') { depth++; i++; }
      else if (c === '}') { if (--depth === 0) { i++; return; } i++; }
      else i++;
    }
  }
  while (i < code.length) {
    const c = code[i];
    if (c === '/' && code[i + 1] === '/') { while (i < code.length && code[i] !== '\n') i++; }
    else if (c === '/' && code[i + 1] === '*') { i = code.indexOf('*/', i + 2) + 2; }
    else if (c === "'" || c === '"') add(str(c), src);
    else if (c === '`') add(tpl(), src);
    else i++;
  }
}

// ---------- C#: "…", $"…{x}…" (con cadenas dentro de las llaves); @"…" no (son rutas) ----------
function scanCs(code, src) {
  let i = 0;
  function plain() { let s = ''; i++; while (i < code.length && code[i] !== '"') { if (code[i] === '\\') { s += code[i + 1]; i += 2; } else s += code[i++]; } i++; return s; }
  function verbatim() { i += 2; while (i < code.length) { if (code[i] === '"' && code[i + 1] === '"') i += 2; else if (code[i] === '"') { i++; return; } else i++; } }
  function interp(verb) {
    let s = '', n = 0; i += verb ? 3 : 2;
    while (i < code.length) {
      const c = code[i];
      if (!verb && c === '\\') { s += code[i + 1]; i += 2; continue; }
      if (verb && c === '"' && code[i + 1] === '"') { s += '"'; i += 2; continue; }
      if (c === '"') { i++; break; }
      if (c === '{' && code[i + 1] === '{') { s += '{'; i += 2; continue; }
      if (c === '}' && code[i + 1] === '}') { s += '}'; i += 2; continue; }
      if (c === '{') {
        i++; let depth = 1;
        while (i < code.length && depth > 0) {
          if (code[i] === '"') add(plain(), src);
          else if (code[i] === '$' && code[i + 1] === '"') add(interp(false), src);
          else { if (code[i] === '{') depth++; if (code[i] === '}') depth--; i++; }
        }
        s += `{${n++}}`; continue;
      }
      s += code[i++];
    }
    return s;
  }
  while (i < code.length) {
    const c = code[i];
    if (c === '/' && code[i + 1] === '/') { while (i < code.length && code[i] !== '\n') i++; }
    else if (c === '/' && code[i + 1] === '*') { i = code.indexOf('*/', i + 2) + 2; }
    else if (c === "'" ) { i += code[i + 1] === '\\' ? 4 : 3; }
    else if (c === '$' && code[i + 1] === '@' && code[i + 2] === '"') add(interp(true), src);
    else if (c === '@' && code[i + 1] === '$' && code[i + 2] === '"') add(interp(true), src);
    else if (c === '$' && code[i + 1] === '"') add(interp(false), src);
    else if (c === '@' && code[i + 1] === '"') verbatim();
    else if (c === '"') add(plain(), src);
    else i++;
  }
}

// ---------- HTML: texto entre etiquetas y atributos visibles ----------
function scanHtml(html, src) {
  html = html.replace(/<script[\s\S]*?<\/script>/g, '').replace(/<style[\s\S]*?<\/style>/g, '').replace(/<svg[\s\S]*?<\/svg>/g, '');
  for (const m of html.matchAll(/>([^<]+)</g)) add(m[1].replace(/&amp;/g, '&').replace(/&nbsp;/g, ' '), src);
  for (const m of html.matchAll(/\b(title|aria-label|placeholder|alt)="([^"]+)"/g)) add(m[2], src);
}

scanHtml(fs.readFileSync(path.join(root, 'wwwroot', 'index.html'), 'utf8'), 'index.html');
scanJs(fs.readFileSync(path.join(root, 'wwwroot', 'app.js'), 'utf8'), 'app.js');
for (const f of fs.readdirSync(root).filter((f) => f.endsWith('.cs'))) scanCs(fs.readFileSync(path.join(root, f), 'utf8'), f);
for (const t of JSON.parse(fs.readFileSync(path.join(root, 'tweaks.json'), 'utf8')))
  for (const k of ['title', 'detail', 'ask', 'group', 'source']) if (t[k]) add(t[k], 'tweaks.json');
// catálogo de apps: nombre y descripción se ven en la vista Apps (los nombres de paquete no se traducen)
for (const t of JSON.parse(fs.readFileSync(path.join(root, 'apps.json'), 'utf8')).apps || [])
  for (const k of ['name', 'detail']) if (t[k]) add(t[k], 'apps.json');

const list = [...out.keys()].sort((a, b) => a.localeCompare(b, 'es'));
const dir = path.join(root, 'wwwroot', 'i18n');
fs.mkdirSync(dir, { recursive: true });
fs.writeFileSync(path.join(dir, 'strings.json'), JSON.stringify(list, null, 1) + '\n');
const bySrc = {};
for (const [k, v] of out) for (const s of v) bySrc[s] = (bySrc[s] || 0) + 1;
console.log(list.length, 'textos;', list.reduce((a, s) => a + s.length, 0), 'caracteres');
console.log(bySrc);
