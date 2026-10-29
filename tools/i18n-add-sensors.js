// Traducciones de los avisos de «Sensores de Vigía» (temperatura del procesador sin permisos de administrador).
// Igual que i18n-add-privacy.js: solo añade lo que falta. Uso: node tools/i18n-add-sensors.js
'use strict';
const fs = require('fs');
const path = require('path');
const dir = path.join(__dirname, '..', 'app', 'Vigia', 'wwwroot', 'i18n');
const T = {
  "Sensores de Vigía no está activo, así que sin permisos no se ven los grados del procesador. Abre Puente y usa Reparar.": [
    "Vigía Sensors is not running, so the processor temperature can't be read without admin rights. Open Puente and use Repair.",
    "Os Sensores do Vigía não estão ativos, então sem permissões não dá para ver a temperatura do processador. Abra o Puente e use Reparar.",
    "Les capteurs de Vigía ne sont pas actifs : sans droits d'administrateur, la température du processeur n'est pas visible. Ouvrez Puente et utilisez Réparer.",
    "Vigía-Sensoren laufen nicht, daher ist die Prozessortemperatur ohne Administratorrechte nicht sichtbar. Öffne Puente und wähle Reparieren."],
  "Sensores de Vigía no está respondiendo. Si sigue así, abre Puente y usa Reparar.": [
    "Vigía Sensors is not responding. If it keeps happening, open Puente and use Repair.",
    "Os Sensores do Vigía não estão respondendo. Se continuar assim, abra o Puente e use Reparar.",
    "Les capteurs de Vigía ne répondent pas. Si cela continue, ouvrez Puente et utilisez Réparer.",
    "Vigía-Sensoren antworten nicht. Wenn das so bleibt, öffne Puente und wähle Reparieren."],
  "Se ignoró una fuente de sensores que no es de Vigía.": [
    "A sensor source that isn't Vigía's was ignored.",
    "Uma fonte de sensores que não é do Vigía foi ignorada.",
    "Une source de capteurs qui n'est pas celle de Vigía a été ignorée.",
    "Eine Sensorquelle, die nicht von Vigía stammt, wurde ignoriert."],
  "Sensores de Vigía está activo, pero este procesador no entrega una temperatura válida.": [
    "Vigía Sensors is running, but this processor doesn't report a valid temperature.",
    "Os Sensores do Vigía estão ativos, mas este processador não informa uma temperatura válida.",
    "Les capteurs de Vigía sont actifs, mais ce processeur ne fournit pas de température valide.",
    "Vigía-Sensoren laufen, aber dieser Prozessor liefert keine gültige Temperatur."],
  "Hay un juego abierto: la preparación de sensores queda aplazada.": [
    "A game is open: sensor setup is postponed.",
    "Há um jogo aberto: a preparação dos sensores fica adiada.",
    "Un jeu est ouvert : la préparation des capteurs est reportée.",
    "Ein Spiel ist geöffnet: die Einrichtung der Sensoren wird verschoben."],
  "Falta el componente oficial de sensores en la instalación. Usa Reparar.": [
    "The official sensor component is missing from the installation. Use Repair.",
    "Falta o componente oficial de sensores na instalação. Use Reparar.",
    "Le composant officiel des capteurs manque dans l'installation. Utilisez Réparer.",
    "Die offizielle Sensorkomponente fehlt in der Installation. Wähle Reparieren."],
  "La preparación de sensores terminó con código {0}; dependencia no confirmada. Usa Reparar.": [
    "Sensor setup ended with code {0}; dependency not confirmed. Use Repair.",
    "A preparação dos sensores terminou com o código {0}; dependência não confirmada. Use Reparar.",
    "La préparation des capteurs s'est terminée avec le code {0} ; dépendance non confirmée. Utilisez Réparer.",
    "Die Sensoreinrichtung endete mit Code {0}; Abhängigkeit nicht bestätigt. Wähle Reparieren."],
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
