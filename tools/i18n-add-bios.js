// Traducciones de los avisos de cambios en la BIOS (comparación con la última vez, solo lectura).
// Igual que i18n-add-privacy.js: solo añade lo que falta. Uso: node tools/i18n-add-bios.js
'use strict';
const fs = require('fs');
const path = require('path');
const dir = path.join(__dirname, '..', 'app', 'Vigia', 'wwwroot', 'i18n');
const T = {
  "La BIOS cambió": ["The BIOS changed", "A BIOS mudou", "Le BIOS a changé", "Das BIOS hat sich geändert"],
  "Antes tenías la {0}, ahora la {1}. Al actualizarla, la BIOS vuelve a los valores de fábrica: revisa abajo la RAM, Secure Boot y TPM.": [
    "You had {0} before, now {1}. Updating the BIOS puts it back to factory settings: check RAM, Secure Boot and TPM below.",
    "Antes você tinha a {0}, agora a {1}. Ao atualizar, a BIOS volta às configurações de fábrica: confira abaixo a RAM, o Secure Boot e o TPM.",
    "Vous aviez la {0}, maintenant la {1}. Une mise à jour remet le BIOS aux réglages d'usine : vérifiez ci-dessous la RAM, Secure Boot et le TPM.",
    "Vorher hattest du {0}, jetzt {1}. Ein Update setzt das BIOS auf Werkseinstellungen zurück: prüfe unten RAM, Secure Boot und TPM."],
  "Cambió desde la última vez": ["Changed since last time", "Mudou desde a última vez", "A changé depuis la dernière fois", "Seit dem letzten Mal geändert"],
  "{0}: estaba bien y ahora no. Suele pasar al actualizar o restablecer la BIOS.": [
    "{0}: it was fine before and now it isn't. This usually happens after updating or resetting the BIOS.",
    "{0}: estava certo e agora não está. Costuma acontecer ao atualizar ou redefinir a BIOS.",
    "{0} : c'était bon avant et ça ne l'est plus. Cela arrive souvent après une mise à jour ou une réinitialisation du BIOS.",
    "{0}: war vorher in Ordnung und jetzt nicht mehr. Das passiert oft nach einem BIOS-Update oder -Reset."],
};
const langs = ['en', 'pt', 'fr', 'de'];
for (const [i, lang] of langs.entries()) {
  const f = path.join(dir, `${lang}.json`);
  const raw = fs.readFileSync(f, 'utf8');
  const bom = raw.charCodeAt(0) === 0xfeff;
  const d = JSON.parse(bom ? raw.slice(1) : raw);
  let added = 0;
  for (const [es, tr] of Object.entries(T)) {
    if (tr.length !== 4) throw new Error(`faltan idiomas: ${es}`);
    if (!(es in d)) { d[es] = tr[i]; added++; }
  }
  fs.writeFileSync(f, (bom ? '\ufeff' : '') + JSON.stringify(d));
  console.log(`${lang}: +${added}`);
}
