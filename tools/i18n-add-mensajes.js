// Traduce los mensajes que el usuario sí puede ver y que se habían quedado solo en español
// (errores de copias, del ayudante con permisos, de rutas, de Puente, la pregunta frecuente de seguridad).
// Uso: node tools/i18n-add-mensajes.js
'use strict';
const fs = require('fs');
const path = require('path');
const dir = path.join(__dirname, '..', 'app', 'Vigia', 'wwwroot', 'i18n');
const T = {
 "{0} es un enlace": [
  "{0} is a link",
  "{0} é um link",
  "{0} est un lien",
  "{0} ist eine Verknüpfung"
 ],
 "Esa carpeta es de otra cuenta.": [
  "That folder belongs to another account.",
  "Essa pasta é de outra conta.",
  "Ce dossier appartient à un autre compte.",
  "Dieser Ordner gehört einem anderen Konto."
 ],
 "Esa carpeta es de Windows o de programas; elige otra.": [
  "That folder belongs to Windows or to programs; pick another one.",
  "Essa pasta é do Windows ou de programas; escolha outra.",
  "Ce dossier appartient à Windows ou aux programmes ; choisis-en un autre.",
  "Dieser Ordner gehört zu Windows oder zu Programmen; wähle einen anderen."
 ],
 "Esa carpeta es un enlace a otro sitio; elige una carpeta normal.": [
  "That folder is a link to somewhere else; pick a normal folder.",
  "Essa pasta é um link para outro lugar; escolha uma pasta normal.",
  "Ce dossier est un lien vers un autre endroit ; choisis un dossier normal.",
  "Dieser Ordner verweist woandershin; wähle einen normalen Ordner."
 ],
 "Esa operación no está permitida.": [
  "That operation is not allowed.",
  "Essa operação não é permitida.",
  "Cette opération n'est pas autorisée.",
  "Dieser Vorgang ist nicht erlaubt."
 ],
 "Ese archivo es un enlace; elige el archivo de verdad.": [
  "That file is a link; pick the real file.",
  "Esse arquivo é um link; escolha o arquivo de verdade.",
  "Ce fichier est un lien ; choisis le vrai fichier.",
  "Diese Datei ist eine Verknüpfung; wähle die echte Datei."
 ],
 "Ese archivo no existe.": [
  "That file doesn't exist.",
  "Esse arquivo não existe.",
  "Ce fichier n'existe pas.",
  "Diese Datei existiert nicht."
 ],
 "no existe": [
  "doesn't exist",
  "não existe",
  "n'existe pas",
  "existiert nicht"
 ],
 "No pude revisar esa carpeta.": [
  "I couldn't check that folder.",
  "Não consegui verificar essa pasta.",
  "Je n'ai pas pu vérifier ce dossier.",
  "Ich konnte diesen Ordner nicht prüfen."
 ],
 "Ruta no válida.": [
  "Invalid path.",
  "Caminho inválido.",
  "Chemin non valide.",
  "Ungültiger Pfad."
 ],
 "Solo archivos .iso.": [
  "Only .iso files.",
  "Somente arquivos .iso.",
  "Fichiers .iso uniquement.",
  "Nur .iso-Dateien."
 ],
 "Usa una ruta local completa (C:\\…).": [
  "Use a full local path (C:\\…).",
  "Use um caminho local completo (C:\\…).",
  "Utilise un chemin local complet (C:\\…).",
  "Verwende einen vollständigen lokalen Pfad (C:\\…)."
 ],
 "valor {0} no válido": [
  "invalid value {0}",
  "valor {0} inválido",
  "valeur {0} non valide",
  "ungültiger Wert {0}"
 ],
 "sin permisos de administrador": [
  "without administrator rights",
  "sem permissões de administrador",
  "sans droits d'administrateur",
  "ohne Administratorrechte"
 ],
 "(sin nombre)": [
  "(no name)",
  "(sem nome)",
  "(sans nom)",
  "(ohne Namen)"
 ],
 "arranque de servicio {0} no válido": [
  "invalid service start type {0}",
  "tipo de inicialização de serviço {0} inválido",
  "type de démarrage de service {0} non valide",
  "ungültiger Dienst-Starttyp {0}"
 ],
 "arranque de servicio no permitido": [
  "service start type not allowed",
  "tipo de inicialização de serviço não permitido",
  "type de démarrage de service non autorisé",
  "Dienst-Starttyp nicht erlaubt"
 ],
 "clave de registro no válida": [
  "invalid registry key",
  "chave do registro inválida",
  "clé de registre non valide",
  "ungültiger Registrierungsschlüssel"
 ],
 "contenido demasiado grande": [
  "content too large",
  "conteúdo grande demais",
  "contenu trop volumineux",
  "Inhalt zu groß"
 ],
 "contenido no válido": [
  "invalid content",
  "conteúdo inválido",
  "contenu non valide",
  "ungültiger Inhalt"
 ],
 "datos con forma rara": [
  "data in an unexpected shape",
  "dados com formato estranho",
  "données au format inattendu",
  "Daten in unerwarteter Form"
 ],
 "el archivo es un enlace": [
  "the file is a link",
  "o arquivo é um link",
  "le fichier est un lien",
  "die Datei ist eine Verknüpfung"
 ],
 "esa clave de registro no es de las que guarda Vigía": [
  "that registry key isn't one Vigía backs up",
  "essa chave do registro não é das que o Vigía guarda",
  "cette clé de registre ne fait pas partie de celles que Vigía sauvegarde",
  "dieser Registrierungsschlüssel gehört nicht zu denen, die Vigía sichert"
 ],
 "esa copia volvería a apagar una protección de Windows; sin el sello de Vigía no se hace": [
  "that backup would turn a Windows protection off again; without Vigía's seal it won't be done",
  "essa cópia desligaria de novo uma proteção do Windows; sem o selo do Vigía isso não é feito",
  "cette copie désactiverait à nouveau une protection de Windows ; sans le sceau de Vigía, ce n'est pas fait",
  "diese Sicherung würde einen Windows-Schutz wieder abschalten; ohne Vigías Siegel passiert das nicht"
 ],
 "ese archivo no es de los que guarda Vigía": [
  "that file isn't one Vigía backs up",
  "esse arquivo não é dos que o Vigía guarda",
  "ce fichier ne fait pas partie de ceux que Vigía sauvegarde",
  "diese Datei gehört nicht zu denen, die Vigía sichert"
 ],
 "ese servicio no existe": [
  "that service doesn't exist",
  "esse serviço não existe",
  "ce service n'existe pas",
  "dieser Dienst existiert nicht"
 ],
 "estado de la tarea no válido": [
  "invalid task state",
  "estado da tarefa inválido",
  "état de la tâche non valide",
  "ungültiger Aufgabenstatus"
 ],
 "imagen de fondo no válida": [
  "invalid wallpaper image",
  "imagem de fundo inválida",
  "image de fond d'écran non valide",
  "ungültiges Hintergrundbild"
 ],
 "la copia no tiene la lista de cambios.": [
  "the backup doesn't have the list of changes.",
  "a cópia não tem a lista de alterações.",
  "la copie n'a pas la liste des modifications.",
  "der Sicherung fehlt die Liste der Änderungen."
 ],
 "la copia tiene demasiados cambios.": [
  "the backup has too many changes.",
  "a cópia tem alterações demais.",
  "la copie contient trop de modifications.",
  "die Sicherung enthält zu viele Änderungen."
 ],
 "la ruta pasa por un enlace": [
  "the path goes through a link",
  "o caminho passa por um link",
  "le chemin passe par un lien",
  "der Pfad führt über eine Verknüpfung"
 ],
 "no es una imagen": [
  "it's not an image",
  "não é uma imagem",
  "ce n'est pas une image",
  "das ist kein Bild"
 ],
 "nombre de paquete no válido": [
  "invalid package name",
  "nome de pacote inválido",
  "nom de paquet non valide",
  "ungültiger Paketname"
 ],
 "nombre no válido": [
  "invalid name",
  "nome inválido",
  "nom non valide",
  "ungültiger Name"
 ],
 "pantalla no válida": [
  "invalid display",
  "tela inválida",
  "écran non valide",
  "ungültiger Bildschirm"
 ],
 "raíz de registro no permitida": [
  "registry root not allowed",
  "raiz do registro não permitida",
  "racine de registre non autorisée",
  "Registrierungsstamm nicht erlaubt"
 ],
 "resolución no válida": [
  "invalid resolution",
  "resolução inválida",
  "résolution non valide",
  "ungültige Auflösung"
 ],
 "ruta no válida": [
  "invalid path",
  "caminho inválido",
  "chemin non valide",
  "ungültiger Pfad"
 ],
 "tamaño": [
  "size",
  "tamanho",
  "taille",
  "Größe"
 ],
 "tarea no válida": [
  "invalid task",
  "tarefa inválida",
  "tâche non valide",
  "ungültige Aufgabe"
 ],
 "tipo de cambio desconocido": [
  "unknown kind of change",
  "tipo de alteração desconhecido",
  "type de modification inconnu",
  "unbekannte Art von Änderung"
 ],
 "un acceso en el Inicio de todos los usuarios solo se repone desde una copia sellada por Vigía": [
  "a shortcut in the all-users Startup folder is only put back from a backup sealed by Vigía",
  "um atalho na Inicialização de todos os usuários só é reposto a partir de uma cópia selada pelo Vigía",
  "un raccourci dans le Démarrage de tous les utilisateurs n'est remis qu'à partir d'une copie scellée par Vigía",
  "eine Verknüpfung im Autostart aller Benutzer wird nur aus einer von Vigía versiegelten Sicherung wiederhergestellt"
 ],
 "un cambio de la copia está dañado.": [
  "one change in the backup is damaged.",
  "uma alteração da cópia está danificada.",
  "une modification de la copie est endommagée.",
  "eine Änderung in der Sicherung ist beschädigt."
 ],
 "valor de inicio no válido": [
  "invalid startup value",
  "valor de inicialização inválido",
  "valeur de démarrage non valide",
  "ungültiger Autostart-Wert"
 ],
 "valor de servicio no permitido": [
  "service value not allowed",
  "valor de serviço não permitido",
  "valeur de service non autorisée",
  "Dienstwert nicht erlaubt"
 ],
 "valor guardado no válido": [
  "invalid saved value",
  "valor salvo inválido",
  "valeur enregistrée non valide",
  "ungültiger gespeicherter Wert"
 ],
 "valor no válido": [
  "invalid value",
  "valor inválido",
  "valeur non valide",
  "ungültiger Wert"
 ],
 "volver a poner un programa que arranca para todos los usuarios necesita una copia sellada por Vigía": [
  "putting back a program that starts for all users needs a backup sealed by Vigía",
  "recolocar um programa que inicia para todos os usuários exige uma cópia selada pelo Vigía",
  "remettre un programme qui démarre pour tous les utilisateurs nécessite une copie scellée par Vigía",
  "ein Programm, das für alle Benutzer startet, lässt sich nur mit einer von Vigía versiegelten Sicherung wiederherstellen"
 ],
 "Esa copia está dañada; no cambié nada.": [
  "That backup is damaged; I didn't change anything.",
  "Essa cópia está danificada; não mudei nada.",
  "Cette copie est endommagée ; je n'ai rien changé.",
  "Diese Sicherung ist beschädigt; ich habe nichts geändert."
 ],
 "Esa copia no se puede leer con seguridad.": [
  "That backup can't be read safely.",
  "Essa cópia não pode ser lida com segurança.",
  "Cette copie ne peut pas être lue en toute sécurité.",
  "Diese Sicherung lässt sich nicht sicher lesen."
 ],
 "Esta copia tiene cambios que Vigía no puede restaurar con seguridad, así que no cambié nada.": [
  "This backup has changes Vigía can't restore safely, so I didn't change anything.",
  "Esta cópia tem alterações que o Vigía não consegue restaurar com segurança, então não mudei nada.",
  "Cette copie contient des modifications que Vigía ne peut pas restaurer en toute sécurité, donc je n'ai rien changé.",
  "Diese Sicherung enthält Änderungen, die Vigía nicht sicher wiederherstellen kann, deshalb habe ich nichts geändert."
 ],
 "tipo de cambio desconocido.": [
  "unknown kind of change.",
  "tipo de alteração desconhecido.",
  "type de modification inconnu.",
  "unbekannte Art von Änderung."
 ],
 "Datos no válidos.": [
  "Invalid data.",
  "Dados inválidos.",
  "Données non valides.",
  "Ungültige Daten."
 ],
 "demasiados archivos": [
  "too many files",
  "arquivos demais",
  "trop de fichiers",
  "zu viele Dateien"
 ],
 "El ayudante de Vigía no respondió.": [
  "Vigía's helper didn't respond.",
  "O ajudante do Vigía não respondeu.",
  "L'assistant de Vigía n'a pas répondu.",
  "Vigías Helfer hat nicht geantwortet."
 ],
 "El ayudante de Vigía se cerró.": [
  "Vigía's helper closed.",
  "O ajudante do Vigía fechou.",
  "L'assistant de Vigía s'est fermé.",
  "Vigías Helfer wurde beendet."
 ],
 "La cuenta que dio el permiso no es la tuya. Vigía solo pide permisos a tu propia cuenta (tiene que ser administradora).": [
  "The account that gave permission isn't yours. Vigía only asks your own account for permission (it has to be an administrator).",
  "A conta que deu a permissão não é a sua. O Vigía só pede permissão à sua própria conta (ela precisa ser administradora).",
  "Le compte qui a donné l'autorisation n'est pas le tien. Vigía ne demande l'autorisation qu'à ton propre compte (il doit être administrateur).",
  "Das Konto, das die Erlaubnis gegeben hat, ist nicht deins. Vigía fragt nur dein eigenes Konto um Erlaubnis (es muss ein Administrator sein)."
 ],
 "No sé desde dónde corre Vigía.": [
  "I don't know where Vigía is running from.",
  "Não sei de onde o Vigía está rodando.",
  "Je ne sais pas d'où Vigía s'exécute.",
  "Ich weiß nicht, von wo Vigía läuft."
 ],
 "Para esto Vigía necesita permiso de administrador y no se dio. No cambié nada.": [
  "Vigía needs administrator permission for this and it wasn't given. I didn't change anything.",
  "Para isso o Vigía precisa de permissão de administrador e ela não foi dada. Não mudei nada.",
  "Pour cela, Vigía a besoin de l'autorisation d'administrateur et elle n'a pas été donnée. Je n'ai rien changé.",
  "Dafür braucht Vigía Administratorrechte, und die wurden nicht erteilt. Ich habe nichts geändert."
 ],
 "Se conectó otro programa en lugar del ayudante de Vigía. No le mandé nada.": [
  "Another program connected instead of Vigía's helper. I didn't send it anything.",
  "Outro programa se conectou no lugar do ajudante do Vigía. Não mandei nada para ele.",
  "Un autre programme s'est connecté à la place de l'assistant de Vigía. Je ne lui ai rien envoyé.",
  "Statt Vigías Helfer hat sich ein anderes Programm verbunden. Ich habe ihm nichts geschickt."
 ],
 "Vigía no está en una carpeta protegida, así que no pide permisos de administrador desde ahí. Instálala con Puente (Archivos de programa) y vuelve a intentarlo.": [
  "Vigía isn't in a protected folder, so it won't ask for administrator permission from there. Install it with Puente (Program Files) and try again.",
  "O Vigía não está numa pasta protegida, então não pede permissão de administrador a partir daí. Instale-o com o Puente (Arquivos de Programas) e tente de novo.",
  "Vigía n'est pas dans un dossier protégé, il ne demande donc pas l'autorisation d'administrateur depuis là. Installe-le avec Puente (Program Files) et réessaie.",
  "Vigía liegt nicht in einem geschützten Ordner und fragt von dort keine Administratorrechte an. Installiere es mit Puente (Programme) und versuche es erneut."
 ],
 "Fortnite: ventana a pantalla completa {0} × {1}": [
  "Fortnite: borderless fullscreen {0} × {1}",
  "Fortnite: janela em tela cheia {0} × {1}",
  "Fortnite : fenêtré plein écran {0} × {1}",
  "Fortnite: Vollbild im Fenster {0} × {1}"
 ],
 ", cuenta local": [
  ", local account",
  ", conta local",
  ", compte local",
  ", lokales Konto"
 ],
 "Solo en la máquina virtual de pruebas (VIGIA_LAB=1 y una VM de Hyper-V). No se cambió nada.": [
  "Only in the test virtual machine (VIGIA_LAB=1 and a Hyper-V VM). Nothing was changed.",
  "Somente na máquina virtual de testes (VIGIA_LAB=1 e uma VM do Hyper-V). Nada foi alterado.",
  "Uniquement dans la machine virtuelle de test (VIGIA_LAB=1 et une VM Hyper-V). Rien n'a été modifié.",
  "Nur in der virtuellen Testmaschine (VIGIA_LAB=1 und eine Hyper-V-VM). Es wurde nichts geändert."
 ],
 "{0} servicios de seguridad están deshabilitados ({1}{2}).": [
  "{0} security services are disabled ({1}{2}).",
  "{0} serviços de segurança estão desativados ({1}{2}).",
  "{0} services de sécurité sont désactivés ({1}{2}).",
  "{0} Sicherheitsdienste sind deaktiviert ({1}{2})."
 ],
 "Carpeta o archivo": [
  "Folder or file",
  "Pasta ou arquivo",
  "Dossier ou fichier",
  "Ordner oder Datei"
 ],
 "Extensión": [
  "Extension",
  "Extensão",
  "Extension",
  "Erweiterung"
 ],
 "cambiar el servicio {0}": [
  "change the service {0}",
  "alterar o serviço {0}",
  "modifier le service {0}",
  "den Dienst {0} ändern"
 ],
 "configurar el plan de energía": [
  "set up the power plan",
  "configurar o plano de energia",
  "configurer le mode de gestion de l'alimentation",
  "den Energiesparplan einrichten"
 ],
 "restaurar el servicio": [
  "restore the service",
  "restaurar o serviço",
  "restaurer le service",
  "den Dienst wiederherstellen"
 ],
 "ese ajuste ya no está en el catálogo": [
  "that setting is no longer in the catalog",
  "esse ajuste não está mais no catálogo",
  "ce réglage n'est plus dans le catalogue",
  "diese Einstellung ist nicht mehr im Katalog"
 ],
 "falta lo guardado": [
  "the saved copy is missing",
  "falta o que foi salvo",
  "la sauvegarde est manquante",
  "die Sicherung fehlt"
 ],
 "no se pudo deshacer todo": [
  "not everything could be undone",
  "não foi possível desfazer tudo",
  "tout n'a pas pu être annulé",
  "es ließ sich nicht alles rückgängig machen"
 ],
 "quedó como estaba": [
  "left as it was",
  "ficou como estava",
  "laissé tel quel",
  "blieb, wie es war"
 ],
 "tardó más de 60 s": [
  "took more than 60 s",
  "demorou mais de 60 s",
  "a pris plus de 60 s",
  "dauerte länger als 60 s"
 ],
 "copia original no válida": [
  "invalid original backup",
  "cópia original inválida",
  "copie d'origine non valide",
  "ungültige Originalsicherung"
 ],
 "demasiados valores": [
  "too many values",
  "valores demais",
  "trop de valeurs",
  "zu viele Werte"
 ],
 "plan de energía no válido": [
  "invalid power plan",
  "plano de energia inválido",
  "mode d'alimentation non valide",
  "ungültiger Energiesparplan"
 ],
 "arranque diferido {0} no válido": [
  "invalid delayed start {0}",
  "início atrasado {0} inválido",
  "démarrage différé {0} non valide",
  "ungültiger verzögerter Start {0}"
 ],
 "El Puente publicado ({0}) es más viejo que el que tienes ({1}); no lo cambié.": [
  "The published Puente ({0}) is older than the one you have ({1}); I didn't change it.",
  "O Puente publicado ({0}) é mais antigo que o que você tem ({1}); não o troquei.",
  "Le Puente publié ({0}) est plus ancien que celui que tu as ({1}) ; je ne l'ai pas changé.",
  "Das veröffentlichte Puente ({0}) ist älter als deins ({1}); ich habe es nicht ausgetauscht."
 ],
 "Falta Puente en la carpeta de Vigía. Vuelve a instalar Vigía con Puente.": [
  "Puente is missing from Vigía's folder. Reinstall Vigía with Puente.",
  "Falta o Puente na pasta do Vigía. Instale o Vigía de novo com o Puente.",
  "Puente manque dans le dossier de Vigía. Réinstalle Vigía avec Puente.",
  "Puente fehlt im Ordner von Vigía. Installiere Vigía mit Puente neu."
 ],
 "Puente cambió en disco antes de usarlo: no lo instalé.": [
  "Puente changed on disk before it was used: I didn't install it.",
  "O Puente mudou no disco antes de ser usado: não o instalei.",
  "Puente a changé sur le disque avant d'être utilisé : je ne l'ai pas installé.",
  "Puente hat sich auf der Festplatte geändert, bevor es benutzt wurde: ich habe es nicht installiert."
 ],
 "{0} Tardó {1} min.": [
  "{0} It took {1} min.",
  "{0} Levou {1} min.",
  "{0} Cela a pris {1} min.",
  "{0} Es dauerte {1} Min."
 ],
 "Memoria USB": [
  "USB drive",
  "Pendrive",
  "Clé USB",
  "USB-Stick"
 ],
 "No. Cada cambio se guarda antes de hacerlo y, si Vigía no puede guardar la copia, no cambia nada. Los ajustes que tenían riesgo se quitaron y los experimentales vienen apagados. Vigía nunca escribe en la BIOS, no apaga Defender y no frena las actualizaciones de seguridad.": [
  "No. Every change is backed up before it's made and, if Vigía can't save the backup, it changes nothing. Risky settings were removed and experimental ones come turned off. Vigía never writes to the BIOS, doesn't turn off Defender and doesn't hold back security updates.",
  "Não. Cada alteração é salva antes de ser feita e, se o Vigía não conseguir salvar a cópia, não muda nada. Os ajustes arriscados foram removidos e os experimentais vêm desligados. O Vigía nunca grava na BIOS, não desliga o Defender e não segura as atualizações de segurança.",
  "Non. Chaque modification est sauvegardée avant d'être faite et, si Vigía ne peut pas enregistrer la copie, il ne change rien. Les réglages risqués ont été retirés et les expérimentaux sont désactivés. Vigía n'écrit jamais dans le BIOS, ne désactive pas Defender et ne bloque pas les mises à jour de sécurité.",
  "Nein. Jede Änderung wird vorher gesichert, und wenn Vigía die Sicherung nicht speichern kann, ändert es nichts. Riskante Einstellungen wurden entfernt, experimentelle sind ausgeschaltet. Vigía schreibt nie ins BIOS, schaltet Defender nicht ab und bremst keine Sicherheitsupdates."
 ]
};
// Líneas internas (registro, nombres de servicio que se buscan tal cual): iguales en todos los idiomas.
// Van con entrada exacta porque el traductor, si no las conoce, cambia palabras sueltas («de» -> «of»).
const SAME = [
 "FALTA (se crearía en la gráfica: {0})",
 "Sensores de Vigía:",
 "aplicar {0}: ERROR {1}; {2}",
 "botón de descarga listo",
 "cola de impresión",
 "comando desconocido de la interfaz: {0}",
 "copias: sin clave de sello:",
 "la interfaz es de otra cuenta",
 "liberada caché en espera de baja prioridad: uso {0} %, libre {1} GB -> {2} GB disponibles",
 "mensaje de la interfaz rechazado ({0})",
 "plan de energía: aplicar ({0})",
 "plan de energía: vuelve al valor de antes ({0} = {1})"
];
for (const k of SAME) T[k] = [k, k, k, k];
const langs = ['en', 'pt', 'fr', 'de'];
langs.forEach((l, i) => {
  const f = path.join(dir, `${l}.json`);
  const raw = fs.readFileSync(f, 'utf8');
  const bom = raw.charCodeAt(0) === 0xfeff;
  const d = JSON.parse(bom ? raw.slice(1) : raw);
  let n = 0;
  for (const [k, v] of Object.entries(T)) if (d[k] !== v[i]) { d[k] = v[i]; n++; }
  fs.writeFileSync(f, (bom ? '\ufeff' : '') + JSON.stringify(d));
  console.log(l, n, 'textos');
});
