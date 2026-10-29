// Vigía · landing: idioma, tablero de la portada, versión publicada y compartir. Sin dependencias.
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const URL_SITE = 'https://vigiapc.vercel.app/';

  // ------------------------------------------------------------------ textos
  // El español está escrito en el HTML; aquí van los demás idiomas. Las claves b* son del tablero.
  const ES_EXTRA = {
    title: 'Vigía · Optimización real para tu PC de juego',
    desc: 'Vigía optimiza Windows para jugar: libera RAM, apaga lo que arranca con Windows, ajusta el sistema y crea una ISO limpia. Guarda una copia antes de cada cambio. Gratis y con el código a la vista.',
    shareOk: 'Enlace copiado. Pégalo donde quieras.',
    shareText: 'Vigía: optimiza el PC para jugar y guarda copia de todo. Gratis y de código abierto.',
    bRam: 'Memoria', bRamA: '52 % en uso', bRamB: '16 % en uso',
    bStart: 'Inicio de Windows', bStartA: '9 programas', bStartB: 'solo Vigía',
    bTw: 'Ajustes de Windows', bTwA: 'como vienen', bTwB: 'para jugar',
    bUs: 'Vigía en reposo', bUsV: '0,1 % CPU', bCopy: 'copia guardada',
  };
  const T = {
    en: {
      title: 'Vigía · Real optimization for your gaming PC',
      desc: 'Vigía tunes Windows for gaming: frees RAM, turns off what starts with Windows, adjusts the system and builds a clean ISO. It saves a backup before every change. Free, with the code in the open.',
      skip: 'Skip to content', navAria: 'Sections', navWhat: 'What it does', navCopies: 'Backups', navCode: 'Code', navSupport: 'Support', langLabel: 'Language', download: 'Download',
      heroH: 'Real optimization. Measurable performance.',
      heroLead: 'Vigía frees RAM, turns off what starts with Windows and tunes the system for gaming. Before it touches anything it saves how things were, and you undo it in one click.',
      dlFree: 'Download free', dlMeta: 'Windows 10 and 11 · 64-bit', ghLink: 'Read the code on GitHub',
      t1: 'Open code, not for sale', t2: 'No account, no ads', t3: 'Sends none of your data',
      whatH: 'What it changes on your PC', whatP: 'Every part explains what it changes in plain words. Anything risky comes off and flagged.',
      m1h: 'Simple mode', m1p: 'Answer a few questions (do you game? use a printer? Xbox?) and Vigía does the rest.',
      m2h: 'Memory', m2p: 'Frees RAM on its own when it hits 60%. In one test it gave back 11.2 GB.', m2r: '52% → 16%',
      m3h: 'Windows startup', m3p: 'Turn off everything that starts with Windows in one click.',
      m4h: 'Games', m4p: 'Windows settings for gaming and a Fortnite profile. It only edits config files and never touches anti-cheat.',
      m5h: 'Monitor', m5p: 'Temperature, load and clock using the same counters as Task Manager, plus which program is using too much.',
      m6h: 'Windows ISO', m6p: 'Builds a clean ISO from the official one: local account, no ads, no TPM check, no automatic BitLocker. Then writes it to a USB drive.',
      m7h: 'BIOS', m7p: 'Reads your motherboard and tells you what to enable, like EXPO or Resizable BAR. It never writes to the BIOS.',
      m8h: 'Apps and storage', m8p: 'Removes the Windows apps you don\u2019t need and clears old temp files. Anything in use is left alone.',
      shotsH: 'Vigía screenshots',
      shot1Alt: 'Vigía Simple mode screen: yes or no questions about gaming, printing, Xbox and search.', shot1: 'Simple mode: you answer, Vigía does the rest.',
      shot2Alt: 'Vigía Monitor screen: CPU, GPU and memory with temperature, load and clock, and a one-minute graph.', shot2: 'Monitor: what Task Manager measures, plus temperatures.',
      copiesH: 'Every change leaves a backup',
      c1h: 'Saves how it was', c1p: 'Registry, services, Windows startup or resolution. If it can\u2019t save the backup, it changes nothing.',
      c2h: 'Changes only what you ticked', c2p: 'Medium and high risk settings are tested first in a virtual machine with a clean Windows.',
      c3h: 'Undo in one click', c3p: 'From Backups or from the icon by the clock. \u201cBack to how it was\u201d restores everything.',
      costH: 'Minimized, it\u2019s barely there', costP: 'An optimizer shouldn\u2019t be the thing eating your performance. Minimized, it stops reading sensors and drawing, and gives its memory back to Windows.',
      benchCap: 'Vigía resource use', benchCpu: 'CPU (one core)', benchOpen: 'Open', benchMin: 'Minimized', bOpenCpu: '6.7%', bMinCpu: '0.1%',
      codeH: 'The code is out in the open', codeP: 'Vigía changes things on your system, so you have every right to see how. All the code is on GitHub: read it, build it yourself or suggest changes. Use it and modify it for free; what you can\u2019t do is sell it or charge for it.',
      f1: 'It sends nothing: no telemetry. It only asks GitHub whether there\u2019s a new version, and you can turn that off.',
      f2: 'Updates are signed and the app checks the signature before installing them.',
      f3: 'No third-party drivers, it doesn\u2019t turn off Defender or UAC, and it ships no activators.',
      termAria: 'Build Vigía', termC: ':: build Vigía yourself (.NET 10 SDK)',
      supH: 'Actually free', supP: 'No email, no sign-up, and no paid version keeping the good parts. If Vigía helped you, here\u2019s how to help back:',
      a1h: 'Buy a coffee', a1p: 'On Ko-fi. Optional, and it unlocks nothing.', a2h: 'Share', a2p: 'Send the link to a friend.',
      a3h: 'Tell us what\u2019s broken', a3p: 'Open a report on GitHub.', a4h: 'Tell us what could be better', a4p: 'Ideas go on GitHub too.',
      endH: 'Try it on your PC', smartH: 'If Windows warns you',
      smart: 'The installer isn\u2019t publisher-signed yet, so Windows may show \u201cWindows protected your PC\u201d. Click \u201cMore info\u201d, then \u201cRun anyway\u201d. The installer checks Vigía\u2019s signature before installing it.',
      fCode: 'Source code', fRel: 'All versions', fLic: 'Noncommercial license', fPriv: 'Privacy', fLegal: 'Legal notice',
      shareOk: 'Link copied. Paste it anywhere.', shareText: 'Vigía: tunes your PC for gaming and backs up everything. Free and open source.',
      bRam: 'Memory', bRamA: '52% in use', bRamB: '16% in use', bStart: 'Windows startup', bStartA: '9 programs', bStartB: 'only Vigía',
      bTw: 'Windows settings', bTwA: 'out of the box', bTwB: 'for gaming', bUs: 'Vigía idle', bUsV: '0.1% CPU', bCopy: 'backup saved',
    },
    pt: {
      title: 'Vigía · Otimização real para o seu PC gamer',
      desc: 'O Vigía otimiza o Windows para jogar: libera RAM, desliga o que inicia com o Windows, ajusta o sistema e cria uma ISO limpa. Salva uma cópia antes de cada mudança. Grátis e com o código à vista.',
      skip: 'Pular para o conteúdo', navAria: 'Seções', navWhat: 'O que faz', navCopies: 'Cópias', navCode: 'Código', navSupport: 'Apoie', langLabel: 'Idioma', download: 'Baixar',
      heroH: 'Otimização real. Desempenho que se mede.',
      heroLead: 'O Vigía libera a RAM, desliga o que inicia com o Windows e ajusta o sistema para jogar. Antes de mexer em algo, salva como estava, e você desfaz com um clique.',
      dlFree: 'Baixar grátis', dlMeta: 'Windows 10 e 11 · 64 bits', ghLink: 'Ver o código no GitHub',
      t1: 'Código à vista, não se vende', t2: 'Sem conta nem anúncios', t3: 'Não envia seus dados',
      whatH: 'O que ele mexe no seu PC', whatP: 'Cada parte explica o que muda em palavras simples. O que é arriscado vem desligado e marcado.',
      m1h: 'Modo simples', m1p: 'Você responde umas perguntas (joga? usa impressora? Xbox?) e o Vigía faz o resto.',
      m2h: 'Memória', m2p: 'Libera a RAM sozinho ao chegar a 60 %. Num teste devolveu 11,2 GB.', m2r: '52 % → 16 %',
      m3h: 'Inicialização do Windows', m3p: 'Desliga com um clique tudo o que inicia com o Windows.',
      m4h: 'Jogos', m4p: 'Ajustes do Windows para jogar e perfil para Fortnite. Só edita arquivos de configuração; nunca mexe no anti-cheat.',
      m5h: 'Monitor', m5p: 'Temperatura, carga e clock com os mesmos contadores do Gerenciador de Tarefas, e qual programa gasta demais.',
      m6h: 'ISO do Windows', m6p: 'Cria uma ISO limpa a partir da oficial: conta local, sem anúncios, sem TPM nem BitLocker automático. E grava num pendrive.',
      m7h: 'BIOS', m7p: 'Lê sua placa-mãe e diz o que ativar, como EXPO ou Resizable BAR. Nunca grava na BIOS.',
      m8h: 'Apps e armazenamento', m8p: 'Remove os apps do Windows que sobram e limpa temporários antigos. O que está em uso não é tocado.',
      shotsH: 'Capturas do Vigía',
      shot1Alt: 'Tela Modo simples do Vigía: perguntas de sim ou não sobre jogos, impressora, Xbox e pesquisa.', shot1: 'Modo simples: você responde, o Vigía faz o resto.',
      shot2Alt: 'Tela Monitor do Vigía: CPU, GPU e memória com temperatura, carga e clock, e o gráfico do último minuto.', shot2: 'Monitor: o mesmo que o Gerenciador de Tarefas mede, mais as temperaturas.',
      copiesH: 'Cada mudança deixa sua cópia',
      c1h: 'Salva como estava', c1p: 'Registro, serviços, inicialização ou resolução. Se não consegue salvar a cópia, não muda nada.',
      c2h: 'Muda só o que você marcou', c2p: 'Ajustes de risco médio ou alto são testados antes numa máquina virtual com Windows limpo.',
      c3h: 'Desfaz com um clique', c3p: 'Em Cópias ou no ícone ao lado do relógio. «Voltar como estava» devolve tudo.',
      costH: 'Minimizado, quase não existe', costP: 'Um otimizador não deveria ser o que tira seu desempenho. Minimizado, para de ler sensores e de desenhar, e devolve a memória ao Windows.',
      benchCap: 'Consumo do Vigía', benchCpu: 'CPU (um núcleo)', benchOpen: 'Aberto', benchMin: 'Minimizado', bOpenCpu: '6,7 %', bMinCpu: '0,1 %',
      codeH: 'O código está à vista', codeP: 'O Vigía mexe no seu sistema, então você tem direito de ver como. Todo o código está no GitHub: leia, compile você mesmo ou proponha mudanças. Use e modifique de graça; o que não pode é vender nem cobrar por ele.',
      f1: 'Não envia nada: sem telemetria. Só pergunta ao GitHub se há versão nova, e isso pode ser desligado.',
      f2: 'As atualizações vêm assinadas e o app confere a assinatura antes de instalar.',
      f3: 'Não usa drivers de terceiros, não desliga o Defender nem o UAC, e não traz ativadores.',
      termAria: 'Compilar o Vigía', termC: ':: compile o Vigía você mesmo (SDK do .NET 10)',
      supH: 'Grátis de verdade', supP: 'Não pede e-mail nem cadastro, e não há versão paga guardando o melhor. Se o Vigía te ajudou, dá para ajudar assim:',
      a1h: 'Pagar um café', a1p: 'No Ko-fi. É opcional e não desbloqueia nada.', a2h: 'Compartilhar', a2p: 'Mande o link para um amigo.',
      a3h: 'Conte o que falha', a3p: 'Abra um relato no GitHub.', a4h: 'Conte o que poderia melhorar', a4p: 'As ideias também vão no GitHub.',
      endH: 'Experimente no seu PC', smartH: 'Se o Windows avisar',
      smart: 'O instalador ainda não tem assinatura de editor, então o Windows pode mostrar «O Windows protegeu o computador». Clique em «Mais informações» e depois em «Executar assim mesmo». O instalador confere a assinatura do Vigía antes de instalar.',
      fCode: 'Código-fonte', fRel: 'Todas as versões', fLic: 'Licença não comercial', fPriv: 'Privacidade', fLegal: 'Aviso legal',
      shareOk: 'Link copiado. Cole onde quiser.', shareText: 'Vigía: otimiza o PC para jogar e guarda cópia de tudo. Grátis e de código aberto.',
      bRam: 'Memória', bRamA: '52 % em uso', bRamB: '16 % em uso', bStart: 'Inicialização', bStartA: '9 programas', bStartB: 'só o Vigía',
      bTw: 'Ajustes do Windows', bTwA: 'como vêm', bTwB: 'para jogar', bUs: 'Vigía parado', bUsV: '0,1 % CPU', bCopy: 'cópia salva',
    },
    fr: {
      title: 'Vigía · Une vraie optimisation pour votre PC de jeu',
      desc: 'Vigía optimise Windows pour le jeu : libère la RAM, coupe ce qui démarre avec Windows, règle le système et crée une ISO propre. Une copie avant chaque changement. Gratuit, code visible par tous.',
      skip: 'Aller au contenu', navAria: 'Sections', navWhat: 'Ce qu\u2019il fait', navCopies: 'Copies', navCode: 'Code', navSupport: 'Soutenir', langLabel: 'Langue', download: 'Télécharger',
      heroH: 'Une vraie optimisation. Des performances mesurables.',
      heroLead: 'Vigía libère la RAM, coupe ce qui démarre avec Windows et règle le système pour le jeu. Avant de toucher à quoi que ce soit, il garde l\u2019état d\u2019origine, et vous annulez en un clic.',
      dlFree: 'Télécharger gratuitement', dlMeta: 'Windows 10 et 11 · 64 bits', ghLink: 'Voir le code sur GitHub',
      t1: 'Code ouvert, pas à vendre', t2: 'Sans compte ni pub', t3: 'N\u2019envoie aucune donnée',
      whatH: 'Ce qu\u2019il touche sur votre PC', whatP: 'Chaque partie explique ce qu\u2019elle change avec des mots simples. Ce qui est risqué est désactivé et signalé.',
      m1h: 'Mode simple', m1p: 'Vous répondez à quelques questions (vous jouez ? imprimante ? Xbox ?) et Vigía fait le reste.',
      m2h: 'Mémoire', m2p: 'Libère la RAM tout seul à 60 %. Lors d\u2019un test, il a rendu 11,2 Go.', m2r: '52 % → 16 %',
      m3h: 'Démarrage de Windows', m3p: 'Coupe en un clic tout ce qui démarre avec Windows.',
      m4h: 'Jeux', m4p: 'Réglages Windows pour le jeu et profil Fortnite. Il ne modifie que des fichiers de configuration, jamais l\u2019anti-triche.',
      m5h: 'Moniteur', m5p: 'Température, charge et fréquence avec les mêmes compteurs que le Gestionnaire des tâches, et le programme qui consomme trop.',
      m6h: 'ISO de Windows', m6p: 'Crée une ISO propre à partir de l\u2019officielle : compte local, sans pub, sans TPM ni BitLocker automatique. Puis l\u2019écrit sur une clé USB.',
      m7h: 'BIOS', m7p: 'Lit votre carte mère et vous dit quoi activer, comme EXPO ou Resizable BAR. Il n\u2019écrit jamais dans le BIOS.',
      m8h: 'Applis et stockage', m8p: 'Retire les applis Windows en trop et nettoie les vieux fichiers temporaires. Ce qui est utilisé n\u2019est pas touché.',
      shotsH: 'Captures de Vigía',
      shot1Alt: 'Écran Mode simple de Vigía : questions oui ou non sur le jeu, l\u2019imprimante, la Xbox et la recherche.', shot1: 'Mode simple : vous répondez, Vigía fait le reste.',
      shot2Alt: 'Écran Moniteur de Vigía : CPU, GPU et mémoire avec température, charge et fréquence, et le graphique de la dernière minute.', shot2: 'Moniteur : ce que mesure le Gestionnaire des tâches, plus les températures.',
      copiesH: 'Chaque changement laisse sa copie',
      c1h: 'Garde l\u2019état d\u2019origine', c1p: 'Registre, services, démarrage ou résolution. S\u2019il ne peut pas enregistrer la copie, il ne change rien.',
      c2h: 'Change seulement ce que vous cochez', c2p: 'Les réglages à risque moyen ou élevé sont d\u2019abord testés dans une machine virtuelle avec un Windows propre.',
      c3h: 'Annulez en un clic', c3p: 'Depuis Copies ou l\u2019icône près de l\u2019horloge. « Revenir comme avant » remet tout.',
      costH: 'Réduit, il se fait oublier', costP: 'Un optimiseur ne devrait pas être ce qui vous coûte des performances. Réduit, il arrête de lire les capteurs et de dessiner, et rend sa mémoire à Windows.',
      benchCap: 'Consommation de Vigía', benchCpu: 'CPU (un cœur)', benchOpen: 'Ouvert', benchMin: 'Réduit', bOpenCpu: '6,7 %', bMinCpu: '0,1 %',
      codeH: 'Le code est visible', codeP: 'Vigía modifie votre système, vous avez donc le droit de voir comment. Tout le code est sur GitHub : lisez-le, compilez-le vous-même ou proposez des changements. Utilisez-le et modifiez-le gratuitement ; le vendre ou le faire payer n\u2019est pas permis.',
      f1: 'Il n\u2019envoie rien : aucune télémétrie. Il demande seulement à GitHub s\u2019il y a une nouvelle version, et ça se désactive.',
      f2: 'Les mises à jour sont signées et l\u2019appli vérifie la signature avant de les installer.',
      f3: 'Aucun pilote tiers, il ne coupe ni Defender ni l\u2019UAC, et n\u2019inclut aucun activateur.',
      termAria: 'Compiler Vigía', termC: ':: compiler Vigía vous-même (SDK .NET 10)',
      supH: 'Vraiment gratuit', supP: 'Pas d\u2019e-mail, pas d\u2019inscription, et pas de version payante qui garde le meilleur. Si Vigía vous a servi, voici comment aider :',
      a1h: 'Offrir un café', a1p: 'Sur Ko-fi. C\u2019est facultatif et ça ne débloque rien.', a2h: 'Partager', a2p: 'Envoyez le lien à un ami.',
      a3h: 'Dites-nous ce qui ne marche pas', a3p: 'Ouvrez un rapport sur GitHub.', a4h: 'Dites-nous ce qui pourrait être mieux', a4p: 'Les idées vont aussi sur GitHub.',
      endH: 'Essayez-le sur votre PC', smartH: 'Si Windows vous avertit',
      smart: 'L\u2019installateur n\u2019a pas encore de signature d\u2019éditeur, Windows peut donc afficher « Windows a protégé votre ordinateur ». Cliquez sur « Informations complémentaires » puis « Exécuter quand même ». L\u2019installateur vérifie la signature de Vigía avant de l\u2019installer.',
      fCode: 'Code source', fRel: 'Toutes les versions', fLic: 'Licence non commerciale', fPriv: 'Confidentialité', fLegal: 'Mentions légales',
      shareOk: 'Lien copié. Collez-le où vous voulez.', shareText: 'Vigía : optimise le PC pour le jeu et garde une copie de tout. Gratuit et open source.',
      bRam: 'Mémoire', bRamA: '52 % utilisée', bRamB: '16 % utilisée', bStart: 'Démarrage', bStartA: '9 programmes', bStartB: 'Vigía seul',
      bTw: 'Réglages Windows', bTwA: 'd\u2019origine', bTwB: 'pour jouer', bUs: 'Vigía au repos', bUsV: '0,1 % CPU', bCopy: 'copie gardée',
    },
    de: {
      title: 'Vigía · Echte Optimierung für deinen Gaming-PC',
      desc: 'Vigía optimiert Windows fürs Spielen: gibt RAM frei, schaltet Autostart-Programme ab, passt das System an und erstellt eine saubere ISO. Vor jeder Änderung wird gesichert. Kostenlos, mit offenem Code.',
      skip: 'Zum Inhalt', navAria: 'Abschnitte', navWhat: 'Was es macht', navCopies: 'Sicherungen', navCode: 'Code', navSupport: 'Unterstützen', langLabel: 'Sprache', download: 'Herunterladen',
      heroH: 'Echte Optimierung. Messbare Leistung.',
      heroLead: 'Vigía gibt RAM frei, schaltet ab, was mit Windows startet, und stellt das System aufs Spielen ein. Bevor es etwas ändert, sichert es den alten Zustand, und du machst es mit einem Klick rückgängig.',
      dlFree: 'Kostenlos herunterladen', dlMeta: 'Windows 10 und 11 · 64 Bit', ghLink: 'Code auf GitHub ansehen',
      t1: 'Offener Code, nicht verkäuflich', t2: 'Ohne Konto, ohne Werbung', t3: 'Sendet keine Daten',
      whatH: 'Was es an deinem PC ändert', whatP: 'Jeder Bereich erklärt in einfachen Worten, was er ändert. Riskantes ist aus und markiert.',
      m1h: 'Einfacher Modus', m1p: 'Du beantwortest ein paar Fragen (spielst du? Drucker? Xbox?) und Vigía erledigt den Rest.',
      m2h: 'Arbeitsspeicher', m2p: 'Gibt RAM von selbst frei, sobald 60 % erreicht sind. In einem Test kamen 11,2 GB zurück.', m2r: '52 % → 16 %',
      m3h: 'Windows-Autostart', m3p: 'Schaltet mit einem Klick alles ab, was mit Windows startet.',
      m4h: 'Spiele', m4p: 'Windows-Einstellungen fürs Spielen und ein Fortnite-Profil. Es ändert nur Konfigurationsdateien, nie den Anti-Cheat.',
      m5h: 'Monitor', m5p: 'Temperatur, Last und Takt mit denselben Zählern wie der Task-Manager, und welches Programm zu viel verbraucht.',
      m6h: 'Windows-ISO', m6p: 'Erstellt aus der offiziellen eine saubere ISO: lokales Konto, ohne Werbung, ohne TPM-Prüfung und ohne automatisches BitLocker. Und schreibt sie auf einen USB-Stick.',
      m7h: 'BIOS', m7p: 'Liest dein Mainboard aus und sagt dir, was du aktivieren solltest, etwa EXPO oder Resizable BAR. Es schreibt nie ins BIOS.',
      m8h: 'Apps und Speicher', m8p: 'Entfernt überflüssige Windows-Apps und löscht alte temporäre Dateien. Was gerade benutzt wird, bleibt.',
      shotsH: 'Screenshots von Vigía',
      shot1Alt: 'Vigía-Bildschirm Einfacher Modus: Ja-Nein-Fragen zu Spielen, Drucker, Xbox und Suche.', shot1: 'Einfacher Modus: Du antwortest, Vigía erledigt den Rest.',
      shot2Alt: 'Vigía-Bildschirm Monitor: CPU, GPU und Speicher mit Temperatur, Last und Takt, dazu ein Ein-Minuten-Diagramm.', shot2: 'Monitor: was der Task-Manager misst, plus Temperaturen.',
      copiesH: 'Jede Änderung hinterlässt eine Sicherung',
      c1h: 'Sichert den alten Zustand', c1p: 'Registry, Dienste, Autostart oder Auflösung. Klappt die Sicherung nicht, ändert es nichts.',
      c2h: 'Ändert nur, was du anhakst', c2p: 'Einstellungen mit mittlerem oder hohem Risiko werden vorher in einer virtuellen Maschine mit frischem Windows getestet.',
      c3h: 'Mit einem Klick zurück', c3p: 'Über Sicherungen oder das Symbol neben der Uhr. „Wie vorher“ stellt alles wieder her.',
      costH: 'Minimiert kaum vorhanden', costP: 'Ein Optimierer sollte nicht selbst die Leistung fressen. Minimiert liest es keine Sensoren mehr, zeichnet nichts und gibt seinen Speicher an Windows zurück.',
      benchCap: 'Verbrauch von Vigía', benchCpu: 'CPU (ein Kern)', benchOpen: 'Geöffnet', benchMin: 'Minimiert', bOpenCpu: '6,7 %', bMinCpu: '0,1 %',
      codeH: 'Der Code liegt offen', codeP: 'Vigía ändert Dinge an deinem System, also darfst du sehen, wie. Der ganze Code liegt auf GitHub: lesen, selbst kompilieren oder Änderungen vorschlagen. Nutzen und verändern ist kostenlos erlaubt, verkaufen oder Geld dafür verlangen nicht.',
      f1: 'Es sendet nichts: keine Telemetrie. Es fragt nur GitHub nach einer neuen Version, und das lässt sich abschalten.',
      f2: 'Updates sind signiert, und die App prüft die Signatur vor der Installation.',
      f3: 'Keine Treiber von Dritten, es schaltet weder Defender noch UAC ab und bringt keine Aktivatoren mit.',
      termAria: 'Vigía kompilieren', termC: ':: Vigía selbst kompilieren (.NET 10 SDK)',
      supH: 'Wirklich kostenlos', supP: 'Keine E-Mail, keine Registrierung und keine Bezahlversion, die das Beste zurückhält. Wenn dir Vigía geholfen hat, kannst du so helfen:',
      a1h: 'Einen Kaffee spendieren', a1p: 'Auf Ko-fi. Freiwillig, und es schaltet nichts frei.', a2h: 'Teilen', a2p: 'Schick den Link an einen Freund.',
      a3h: 'Sag uns, was nicht geht', a3p: 'Erstelle eine Meldung auf GitHub.', a4h: 'Sag uns, was besser sein könnte', a4p: 'Ideen kommen auch auf GitHub.',
      endH: 'Probier es auf deinem PC', smartH: 'Falls Windows warnt',
      smart: 'Das Installationsprogramm hat noch keine Herausgebersignatur, daher zeigt Windows eventuell „Der Computer wurde durch Windows geschützt“. Klicke auf „Weitere Informationen“ und dann auf „Trotzdem ausführen“. Das Installationsprogramm prüft die Signatur von Vigía vor der Installation.',
      fCode: 'Quellcode', fRel: 'Alle Versionen', fLic: 'Nichtkommerzielle Lizenz', fPriv: 'Datenschutz', fLegal: 'Rechtliches',
      shareOk: 'Link kopiert. Füg ihn ein, wo du willst.', shareText: 'Vigía: optimiert den PC fürs Spielen und sichert alles. Kostenlos und Open Source.',
      bRam: 'Speicher', bRamA: '52 % belegt', bRamB: '16 % belegt', bStart: 'Autostart', bStartA: '9 Programme', bStartB: 'nur Vigía',
      bTw: 'Windows-Einstellungen', bTwA: 'Werkszustand', bTwB: 'fürs Spielen', bUs: 'Vigía im Leerlauf', bUsV: '0,1 % CPU', bCopy: 'Sicherung gespeichert',
    },
  };
  const LANGS = ['es', 'en', 'pt', 'fr', 'de'];

  // el español se lee del propio HTML para poder volver a él
  const ES = { ...ES_EXTRA };
  $$('[data-i]').forEach((e) => { ES[e.dataset.i] ??= e.innerHTML; });
  $$('[data-i-alt]').forEach((e) => { ES[e.dataset.iAlt] ??= e.alt; });
  $$('[data-i-aria]').forEach((e) => { ES[e.dataset.iAria] ??= e.getAttribute('aria-label'); });

  function pickLang() {
    try { const s = localStorage.getItem('vigia-lang'); if (LANGS.includes(s)) return s; } catch { /* modo privado */ }
    for (const l of navigator.languages || [navigator.language || 'es']) {
      const p = String(l).slice(0, 2).toLowerCase();
      if (LANGS.includes(p)) return p;
    }
    return 'en';
  }
  let lang = pickLang();
  const t = (k) => (lang === 'es' ? ES : T[lang])[k] ?? ES[k] ?? '';

  function apply() {
    document.documentElement.lang = lang;
    $$('[data-i]').forEach((e) => { e.innerHTML = t(e.dataset.i); });
    $$('[data-i-alt]').forEach((e) => { e.alt = t(e.dataset.iAlt); });
    $$('img[data-shot]').forEach((e) => { const s = `img/${e.dataset.shot}-${lang}.webp`; if (!e.src.endsWith(s)) e.src = s; });   // capturas de la app en el idioma de la página
    $$('[data-i-aria]').forEach((e) => e.setAttribute('aria-label', t(e.dataset.iAria)));
    document.title = t('title');
    $('meta[name="description"]').content = t('desc');
    $('#lang').value = lang;
    drawBoard();
  }
  $('#lang').addEventListener('change', (e) => {
    lang = e.target.value;
    try { localStorage.setItem('vigia-lang', lang); } catch { /* sin almacenamiento */ }
    apply();
  });

  // ------------------------------------------------------------------ tablero isométrico
  // El PC visto desde arriba con el cubo de Vigía. Un bloque que se optimiza sube, se enciende en ámbar
  // y deja abajo su copia punteada; luego vuelve. Solo transform y opacidad: no cuesta nada al navegador.
  const NS = 'http://www.w3.org/2000/svg';
  const A = 44, U = 30, X0 = 400, Y0 = 172, LIFT = 52; // medio ancho de celda, alto por piso, origen, subida
  const P = (i, j) => [X0 + (i - j) * A, Y0 + (i + j) * A / 2];
  const r1 = (n) => Math.round(n * 10) / 10;
  const pts = (a) => a.map((p) => p.map(r1).join(',')).join(' ');
  const el = (tag, attrs = {}, parent) => {
    const n = document.createElementNS(NS, tag);
    for (const k in attrs) n.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(n);
    return n;
  };
  // Tablero 5×5. Los cuatro bloques con nombre están en las esquinas y su etiqueta sale hacia afuera,
  // así ninguna línea cruza otro bloque. [i, j, pisos, etiqueta, retraso]
  const BLOCKS = [
    [0, 0, 3, 'us'], [0, 4, 2, 'ram', 0], [4, 0, 1, 'start', 3.6], [4, 4, 2, 'tw', 7.2],
    [1, 0, 1], [2, 0, 2], [0, 1, 2], [1, 1, 1], [3, 1, 2], [2, 2, 2], [0, 2, 1], [4, 2, 1],
    [1, 3, 1], [3, 3, 1], [2, 4, 1], [4, 3, 2], [3, 4, 1],
  ];
  const LABELS = {
    us:    { side: 'up', name: 'bUs', v: 'bUsV' },
    ram:   { side: 'left', name: 'bRam', a: 'bRamA', b: 'bRamB' },
    start: { side: 'right', name: 'bStart', a: 'bStartA', b: 'bStartB' },
    tw:    { side: 'down', name: 'bTw', a: 'bTwA', b: 'bTwB', copy: 'bCopy' },
  };

  function corners(i, j, h) {
    const [cx, cy] = P(i, j), H = h * U;
    return {
      T: [cx, cy - H], R: [cx + A, cy + A / 2 - H], B: [cx, cy + A - H], L: [cx - A, cy + A / 2 - H],
      gB: [cx, cy + A], gR: [cx + A, cy + A / 2], gL: [cx - A, cy + A / 2],
    };
  }
  const outline = (c) => `M${pts([c.T, c.R, c.B, c.L])}Z M${pts([c.L, c.gL, c.gB, c.gR, c.R])} M${pts([c.B, c.gB])}`;
  function cube(g, c) {
    el('polygon', { class: 'face-l', points: pts([c.L, c.B, c.gB, c.gL]) }, g);
    el('polygon', { class: 'face-r', points: pts([c.B, c.R, c.gR, c.gB]) }, g);
    el('polygon', { class: 'top-f', points: pts([c.T, c.R, c.B, c.L]) }, g);
    el('path', { class: 'edge', d: outline(c) }, g);
  }

  const board = $('#board');
  const small = matchMedia('(max-width: 600px)');
  function drawBoard() {
    if (!board) return;
    board.textContent = '';
    // en pantallas chicas solo los bloques, sin etiquetas (el texto de arriba ya lo cuenta)
    board.setAttribute('viewBox', small.matches ? '170 40 460 330' : '-20 0 840 500');
    const defs = el('defs', {}, board);
    const rg = el('radialGradient', { id: 'pool' }, defs);
    el('stop', { offset: '0', 'stop-color': '#e9b45a', 'stop-opacity': '.34' }, rg);
    el('stop', { offset: '1', 'stop-color': '#e9b45a', 'stop-opacity': '0' }, rg);
    const fade = el('radialGradient', { id: 'fade', gradientUnits: 'userSpaceOnUse', cx: X0, cy: Y0 + 110, r: 430 }, defs);
    el('stop', { offset: '0', 'stop-color': '#fff' }, fade);
    el('stop', { offset: '.55', 'stop-color': '#fff', 'stop-opacity': '.7' }, fade);
    el('stop', { offset: '1', 'stop-color': '#fff', 'stop-opacity': '0' }, fade);
    const mk = el('mask', { id: 'gridmask', maskUnits: 'userSpaceOnUse', x: -600, y: -400, width: 2000, height: 1300 }, defs);
    el('rect', { x: -600, y: -400, width: 2000, height: 1300, fill: 'url(#fade)' }, mk);

    // retícula del suelo, alineada con las celdas
    const grid = el('g', { class: 'grid', mask: 'url(#gridmask)' }, board);
    const N = 12;
    for (let k = -N; k <= N + 1; k++) {
      const a1 = P(k, -N), a2 = P(k, N + 1), b1 = P(-N, k), b2 = P(N + 1, k);
      el('line', { x1: r1(a1[0] - A), y1: r1(a1[1] + A / 2), x2: r1(a2[0] - A), y2: r1(a2[1] + A / 2) }, grid);
      el('line', { x1: r1(b1[0] + A), y1: r1(b1[1] + A / 2), x2: r1(b2[0] + A), y2: r1(b2[1] + A / 2) }, grid);
    }

    const labels = el('g', { class: 'labels' });
    [...BLOCKS].sort((a, b) => (a[0] + a[1]) - (b[0] + b[1]) || a[0] - b[0]).forEach(([i, j, h, tag, delay]) => {
      const anim = tag && tag !== 'us';
      const style = anim ? `--l:${LIFT};--delay:${delay}s` : '';
      const c = corners(i, j, h);
      if (anim) {
        const gg = el('g', { style, class: 'on-g' }, board);
        el('ellipse', { class: 'pool', cx: c.gB[0], cy: c.gB[1] - A / 2, rx: A * 1.6, ry: A * .8 }, gg);
        el('path', { class: 'ghost', d: outline(c) }, gg);
      }
      cube(el('g', { class: (anim ? 'blk on' : '') + (tag === 'us' ? ' us' : ''), style }, board), c);
      if (!tag) return;

      const L = LABELS[tag];
      const lg = el('g', { class: anim ? 'blk lbl on' : 'lbl', style }, labels);
      let from, to, tx, ty, anchor, step = 21;
      if (L.side === 'left') { from = c.L; to = [from[0] - 26, from[1]]; tx = to[0] - 8; ty = to[1] - 5; anchor = 'end'; }
      if (L.side === 'right') { from = c.R; to = [from[0] + 26, from[1]]; tx = to[0] + 8; ty = to[1] - 5; anchor = 'start'; }
      if (L.side === 'up') { from = c.T; to = [from[0], from[1] - 30]; tx = to[0]; ty = to[1] - 30; anchor = 'middle'; }
      if (L.side === 'down') { from = c.gB; to = [from[0], from[1] + 26]; tx = to[0]; ty = to[1] + 19; anchor = 'middle'; }
      el('path', { class: 'lead', d: `M${pts([from, to])}` }, lg);
      el('circle', { class: 'dot', cx: r1(from[0]), cy: r1(from[1]), r: 2.6 }, lg);
      const txt = (cls, y, key) => { el('text', { class: cls, x: r1(tx), y: r1(y), 'text-anchor': anchor }, lg).textContent = t(key); };
      txt('ln', ty, L.name);
      if (L.v) txt('lv now', ty + step, L.v);
      else { txt('lv before m', ty + step, L.a); txt('lv after m', ty + step, L.b); }
      if (L.copy) txt('lc after m', ty + step * 2 - 2, L.copy);
    });
    if (!small.matches) board.appendChild(labels);
  }
  small.addEventListener?.('change', drawBoard);

  // pausa la animación cuando la portada no se ve (pestaña oculta o al bajar)
  if (board && 'IntersectionObserver' in window) {
    let seen = true;
    const sync = () => board.classList.toggle('still', !seen || document.hidden || matchMedia('(prefers-reduced-motion: reduce)').matches);
    new IntersectionObserver(([e]) => { seen = e.isIntersecting; sync(); }).observe(board);
    document.addEventListener('visibilitychange', sync);
  }

  // ------------------------------------------------------------------ cabecera, versión y compartir
  const top = $('.top');
  const onScroll = () => top.classList.toggle('scrolled', scrollY > 8);
  addEventListener('scroll', onScroll, { passive: true }); onScroll();

  // la versión publicada más reciente; si GitHub no responde, queda la escrita
  fetch('https://api.github.com/repos/JcperezDev/vigia-releases/releases/latest')
    .then((r) => (r.ok ? r.json() : null))
    .then((j) => { if (j && /^v\d+\.\d+\.\d+$/.test(j.tag_name)) $$('[data-ver]').forEach((e) => { e.textContent = j.tag_name; }); })
    .catch(() => {});

  $('#share').addEventListener('click', async () => {
    const msg = $('#share-msg'), li = $('#share').parentElement;
    if (navigator.share) {
      try { await navigator.share({ title: 'Vigía', text: t('shareText'), url: URL_SITE }); return; } catch (e) { if (e && e.name === 'AbortError') return; }
    }
    try { await navigator.clipboard.writeText(URL_SITE); msg.textContent = t('shareOk'); li.classList.add('ok'); }
    catch { msg.textContent = URL_SITE; }
    setTimeout(() => { msg.textContent = t('a2p'); li.classList.remove('ok'); }, 4000);
  });

  apply();
  document.documentElement.classList.add('ready');
})();
