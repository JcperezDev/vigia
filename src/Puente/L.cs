using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Puente;

/// <summary>
/// Textos de Puente en el idioma que eligió el usuario en Vigía (settings.json → "lang"; "auto" = el de Windows).
/// Los textos se escriben en español en el código y aquí se traducen al mostrarlos. {0} = parte variable (una versión, un número).
/// Si un texto no está, se ve en español.
/// </summary>
static class L
{
    static string? _lang;
    public static string Lang => _lang ??= Pick();

    static string Pick()
    {
        string? pref = Environment.GetEnvironmentVariable("VIGIA_LANG");   // para probar --render en otro idioma
        try
        {
            var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigia", "settings.json");
            if (pref is null && File.Exists(f) && JsonDocument.Parse(File.ReadAllText(f)).RootElement.TryGetProperty("lang", out var l)) pref = l.GetString();
        }
        catch { /* sin ajustes: idioma de Windows */ }
        if (pref is { Length: > 0 } && pref != "auto") return Dicts.ContainsKey(pref) || pref == "es" ? pref : "es";
        var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return sys == "es" || Dicts.ContainsKey(sys) ? sys : "en";
    }

    /// <summary>Traduce un texto ya armado. Prueba entero, luego como plantilla con {0}, luego frase por frase.</summary>
    public static string T(string s)
    {
        if (Lang == "es" || string.IsNullOrEmpty(s) || !Dicts.TryGetValue(Lang, out var d)) return s;
        if (d.TryGetValue(s, out var t)) return t;
        foreach (var (k, v) in d)
        {
            if (!k.Contains("{0}")) continue;
            var parts = k.Split("{0}");
            var m = Regex.Match(s, "^" + string.Join("(.+?)", parts.Select(Regex.Escape)) + "$", RegexOptions.Singleline);
            if (m.Success) return v.Replace("{0}", m.Groups[1].Value);
        }
        var bits = Regex.Split(s, @"(?<=[.!?]) (?=[A-ZÁÉÍÓÚ¿])");
        if (bits.Length > 1) return string.Join(" ", bits.Select(T));
        return s;
    }

    static Dictionary<string, string> D(params string[] kv)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[Es[i / 2]] = kv[i + 1] ?? kv[i];
        return d;
    }

    // el orden de esta lista es el de cada idioma de abajo
    static readonly string[] Es =
    [
        "Buscando actualizaciones…", "Mirando la última versión publicada.", "Descargando Vigía {0}", "Listo: Vigía {0}",
        "La {0} quedó guardada por si quieres volver a ella.", "Vigía está al día", "Tienes la versión {0}, que es la última.",
        "Abrir Vigía", "Cerrar", "No se pudo terminar", "Vigía sigue como estaba.", "Instalando Vigía {0}", "Actualizando a Vigía {0}",
        "Preparando la descarga.", "Tienes la {0}.", "Comprobando Vigía {0}", "Revisando la firma: solo se instala lo que publicó Vigía.",
        "La firma no coincide.", "El archivo no viene de la publicación de Vigía; no se instaló nada.", "Preparando Vigía {0}",
        "Descomprimiendo.", "No apagues el PC; tarda unos segundos.", "Tienes accesos en el Escritorio y en Inicio.",
        "No hay versión anterior", "Puente guarda la versión anterior cuando actualiza Vigía.", "Todavía no hay ninguna.",
        "¿Volver a Vigía {0}?", "Volver", "Volviendo a Vigía {0}", "Cerrando Vigía.", "La {0} quedó guardada como anterior.",
        "¿Quitar Vigía de este PC?", "Quitar", "Quitando Vigía", "Listo: Vigía quitada", "Puente se borra solo al cerrar esta ventana.",
        "Esperando a que se cierre Vigía", "Cerrando Vigía", "Cancelar", "Todavía no hay ninguna versión publicada.",
        "Este Puente se compiló sin clave pública.", "Quedará guardada y podrás volver a ella.", "Ahora tienes la {0}.",
        "Tus ajustes y las copias de lo que cambió Vigía se quedan en {0}.",
    ];

    static readonly Dictionary<string, Dictionary<string, string>> Dicts = new()
    {
        ["en"] = D(
            "", "Checking for updates…", "", "Looking at the latest published version.", "", "Downloading Vigía {0}", "", "Done: Vigía {0}",
            "", "{0} was kept in case you want to go back to it.", "", "Vigía is up to date", "", "You have version {0}, the latest one.",
            "", "Open Vigía", "", "Close", "", "Couldn't finish", "", "Vigía is still as it was.", "", "Installing Vigía {0}", "", "Updating to Vigía {0}",
            "", "Getting the download ready.", "", "You have {0}.", "", "Checking Vigía {0}", "", "Checking the signature: only what Vigía published gets installed.",
            "", "The signature doesn't match.", "", "The file isn't from Vigía's release; nothing was installed.", "", "Preparing Vigía {0}",
            "", "Unpacking.", "", "Don't turn off the PC; it takes a few seconds.", "", "You have shortcuts on the Desktop and in Start.",
            "", "No previous version", "", "Puente keeps the previous version when it updates Vigía.", "", "There isn't one yet.",
            "", "Go back to Vigía {0}?", "", "Go back", "", "Going back to Vigía {0}", "", "Closing Vigía.", "", "{0} was kept as the previous version.",
            "", "Remove Vigía from this PC?", "", "Remove", "", "Removing Vigía", "", "Done: Vigía removed", "", "Puente deletes itself when you close this window.",
            "", "Waiting for Vigía to close", "", "Closing Vigía", "", "Cancel", "", "There's no published version yet.",
            "", "This Puente was built without a public key.", "", "It'll be kept and you can go back to it.", "", "You now have {0}.",
            "", "Your settings and the backups of what Vigía changed stay in {0}."),
        ["pt"] = D(
            "", "Procurando atualizações…", "", "Vendo a última versão publicada.", "", "Baixando Vigía {0}", "", "Pronto: Vigía {0}",
            "", "A {0} ficou guardada caso queira voltar para ela.", "", "O Vigía está em dia", "", "Você tem a versão {0}, que é a mais recente.",
            "", "Abrir Vigía", "", "Fechar", "", "Não deu para terminar", "", "O Vigía continua como estava.", "", "Instalando Vigía {0}", "", "Atualizando para Vigía {0}",
            "", "Preparando o download.", "", "Você tem a {0}.", "", "Verificando Vigía {0}", "", "Conferindo a assinatura: só instala o que o Vigía publicou.",
            "", "A assinatura não confere.", "", "O arquivo não vem da publicação do Vigía; nada foi instalado.", "", "Preparando Vigía {0}",
            "", "Descompactando.", "", "Não desligue o PC; leva alguns segundos.", "", "Você tem atalhos na Área de Trabalho e no Iniciar.",
            "", "Não há versão anterior", "", "O Puente guarda a versão anterior quando atualiza o Vigía.", "", "Ainda não há nenhuma.",
            "", "Voltar para Vigía {0}?", "", "Voltar", "", "Voltando para Vigía {0}", "", "Fechando o Vigía.", "", "A {0} ficou guardada como anterior.",
            "", "Remover o Vigía deste PC?", "", "Remover", "", "Removendo o Vigía", "", "Pronto: Vigía removido", "", "O Puente se apaga sozinho ao fechar esta janela.",
            "", "Esperando o Vigía fechar", "", "Fechando o Vigía", "", "Cancelar", "", "Ainda não há nenhuma versão publicada.",
            "", "Este Puente foi compilado sem chave pública.", "", "Ela fica guardada e você pode voltar para ela.", "", "Agora você tem a {0}.",
            "", "Suas configurações e as cópias do que o Vigía mudou ficam em {0}."),
        ["fr"] = D(
            "", "Recherche de mises à jour…", "", "On regarde la dernière version publiée.", "", "Téléchargement de Vigía {0}", "", "Terminé : Vigía {0}",
            "", "La {0} est gardée si tu veux y revenir.", "", "Vigía est à jour", "", "Tu as la version {0}, la plus récente.",
            "", "Ouvrir Vigía", "", "Fermer", "", "Impossible de terminer", "", "Vigía reste comme avant.", "", "Installation de Vigía {0}", "", "Mise à jour vers Vigía {0}",
            "", "Préparation du téléchargement.", "", "Tu as la {0}.", "", "Vérification de Vigía {0}", "", "Vérification de la signature : seul ce que Vigía a publié est installé.",
            "", "La signature ne correspond pas.", "", "Le fichier ne vient pas de la publication de Vigía ; rien n'a été installé.", "", "Préparation de Vigía {0}",
            "", "Décompression.", "", "N'éteins pas le PC ; ça prend quelques secondes.", "", "Tu as des raccourcis sur le Bureau et dans Démarrer.",
            "", "Pas de version précédente", "", "Puente garde la version précédente quand il met Vigía à jour.", "", "Il n'y en a pas encore.",
            "", "Revenir à Vigía {0} ?", "", "Revenir", "", "Retour à Vigía {0}", "", "Fermeture de Vigía.", "", "La {0} est gardée comme version précédente.",
            "", "Retirer Vigía de ce PC ?", "", "Retirer", "", "Suppression de Vigía", "", "Terminé : Vigía retiré", "", "Puente s'efface tout seul quand tu fermes cette fenêtre.",
            "", "En attente de la fermeture de Vigía", "", "Fermeture de Vigía", "", "Annuler", "", "Aucune version publiée pour l'instant.",
            "", "Ce Puente a été compilé sans clé publique.", "", "Elle sera gardée et tu pourras y revenir.", "", "Tu as maintenant la {0}.",
            "", "Tes réglages et les sauvegardes de ce que Vigía a changé restent dans {0}."),
        ["de"] = D(
            "", "Suche nach Updates…", "", "Prüfe die neueste veröffentlichte Version.", "", "Lade Vigía {0} herunter", "", "Fertig: Vigía {0}",
            "", "{0} bleibt gespeichert, falls du zurück willst.", "", "Vigía ist aktuell", "", "Du hast Version {0}, die neueste.",
            "", "Vigía öffnen", "", "Schließen", "", "Konnte nicht fertig werden", "", "Vigía bleibt, wie es war.", "", "Installiere Vigía {0}", "", "Aktualisiere auf Vigía {0}",
            "", "Bereite den Download vor.", "", "Du hast {0}.", "", "Prüfe Vigía {0}", "", "Prüfe die Signatur: Installiert wird nur, was Vigía veröffentlicht hat.",
            "", "Die Signatur stimmt nicht.", "", "Die Datei stammt nicht aus Vigías Veröffentlichung; nichts wurde installiert.", "", "Bereite Vigía {0} vor",
            "", "Entpacke.", "", "Schalte den PC nicht aus; das dauert ein paar Sekunden.", "", "Du hast Verknüpfungen auf dem Desktop und im Startmenü.",
            "", "Keine vorherige Version", "", "Puente behält die vorherige Version, wenn es Vigía aktualisiert.", "", "Noch gibt es keine.",
            "", "Zurück zu Vigía {0}?", "", "Zurück", "", "Gehe zurück zu Vigía {0}", "", "Schließe Vigía.", "", "{0} bleibt als vorherige Version gespeichert.",
            "", "Vigía von diesem PC entfernen?", "", "Entfernen", "", "Entferne Vigía", "", "Fertig: Vigía entfernt", "", "Puente löscht sich selbst, wenn du dieses Fenster schließt.",
            "", "Warte, bis Vigía geschlossen ist", "", "Schließe Vigía", "", "Abbrechen", "", "Es gibt noch keine veröffentlichte Version.",
            "", "Dieses Puente wurde ohne öffentlichen Schlüssel gebaut.", "", "Sie bleibt gespeichert und du kannst zu ihr zurück.", "", "Jetzt hast du {0}.",
            "", "Deine Einstellungen und die Sicherungen von Vigías Änderungen bleiben in {0}."),
    };
}
