# Changelog

Tous les changements notables de ce projet sont consignés dans ce fichier.

Le format s'inspire de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et le projet vise le [versionnage sémantique](https://semver.org/lang/fr/).

## [Unreleased]

## [1.1.1] - 2026-06-29

### Fixed
- Nettoyage préventif des `<wedge>` (crescendo/decrescendo) dans le MusicXML
  fusionné avant export vers MuseScore : Audiveris peut produire des éléments
  `<wedge>` sans `timeOffset` résolu quand certaines mesures sont marquées
  « no correct rhythm » — son propre exporteur plante alors sur un tri null et
  laisse des données partielles, ce qui fait rejeter le fichier par MuseScore
  (code 1320). Les nuances sont perdues mais la conversion aboutit ; elles
  peuvent être rajoutées manuellement dans MuseScore.
- En cas d'échec MuseScore persistant (code 1320) malgré ce nettoyage, le
  message d'erreur guide désormais l'utilisateur en français pour corriger le
  projet Audiveris (.omr) à la main : marche à suivre adaptée selon que l'option
  « Conserver le projet Audiveris (.omr) » était cochée ou non.

## [1.1.0] - 2026-06-22

### Added
- Le `Setup.msi` détecte désormais une installation existante de Partition2MuseScore
  et distingue mise à jour (version installée plus ancienne) et réinstallation
  (version identique), avec un dialogue dédié l'indiquant avant que l'ancienne
  installation ne soit remplacée (jamais affiché en silencieux `/quiet`, ni sur une
  première installation). `AllowSameVersionUpgrades="yes"` corrige par ailleurs un
  vrai défaut : sans cette option, relancer le `Setup.msi` d'une version identique
  à celle déjà installée créait une 2e entrée dans Ajout/Suppression de programmes
  (`ProductCode="*"` génère un GUID neuf à chaque build) au lieu de remplacer
  proprement l'installation existante.

## [1.0.1] - 2026-06-22

### Added
- Spinner notes/binaire également visible sur la ligne « Versions détectées »
  pendant une installation/mise à jour winget d'Audiveris ou MuseScore en
  arrière-plan — jusqu'ici, seul un texte statique « ... en cours »
  l'indiquait dans ce cas (l'écran de premier lancement bloquant, lui,
  avait déjà son propre spinner).

## [1.0.0] - 2026-06-21

### Added
- Mise en place initiale du dépôt (licence AGPL v3, gouvernance, structure de
  dossiers `partitions-sources/` / `partitions-convertis/`).
- Application desktop C#/.NET 10 (WPF) : champs source/destination avec
  sélecteurs de fichiers natifs, pré-remplissage de la destination depuis la
  source, bouton de conversion, barre de progression réelle (basée sur les
  logs Audiveris), bouton d'ouverture du dossier de destination en fin de
  conversion.
- Pipeline de conversion complet : Audiveris CLI (`-batch -export`) → fusion
  des mouvements MusicXML détectés (association des parties par nom,
  remplissage par silences des parties absentes d'un mouvement) → MuseScore 4
  CLI (`-o`) → `.mscz`.
- Détection automatique des emplacements d'installation d'Audiveris et
  MuseScore 4 via le registre Windows (`InstallLocation`), avec repli sur
  l'emplacement par défaut.
- Journalisation des erreurs de conversion dans un fichier `.log` horodaté
  à côté de la destination prévue.
- Sélecteur de langue d'OCR (français, anglais, espagnol, italien, allemand,
  latin), transmis à Audiveris via `-constant`.
- Case « Conserver le projet Audiveris (.omr) » : ajoute `-save` à l'appel
  Audiveris et copie le `.omr` produit à côté de la destination, pour
  permettre une correction manuelle avant réexport.
- Champ optionnel de style MuseScore (`.mss`), appliqué à l'export via `-S`.
- Nouvelle tentative automatique (jusqu'à 3 fois) de l'export MuseScore 4 en
  cas d'échec ponctuel non reproductible.
- Arrêt propre des process Audiveris/MuseScore 4 (avec confirmation) si la
  fenêtre est fermée pendant une conversion en cours.
- Détection dynamique des langues OCR disponibles à partir du dossier
  `tessdata` réel d'Audiveris, au lieu d'une liste figée.
- Affichage, au démarrage, de la version installée et de la dernière version
  disponible (dépôts GitHub `Audiveris/audiveris` et `musescore/MuseScore`)
  pour Audiveris et MuseScore. La dernière version connue est mise en cache
  localement pour rester disponible hors connexion.
- Prétraitement des fichiers image (JPG/PNG/TIFF/BMP) avant Audiveris, via
  Magick.NET : niveaux de gris, redressement (deskew), débruitage
  (despeckle), flou gaussien + masque net — pour de meilleurs résultats sur
  des scans/photos de qualité moyenne. Statut « Prétraitement de l'image »
  affiché dans la barre de progression.
- Extraction étendue aux fichiers PDF : chaque page est exportée en PNG à
  300 DPI (`PDFtoImage`/PDFium, sans dépendance externe type Ghostscript),
  convertie en niveaux de gris, puis les pages sont réassemblées en un
  nouveau PDF (Magick.NET) transmis à Audiveris. Préserve le découpage en un
  seul « Book » multi-feuillets — nécessaire à la détection des mouvements à
  cheval sur plusieurs pages — qu'aurait cassé l'envoi des pages séparément
  (vérifié : Audiveris crée alors un Book par fichier). Statut « Extraction
  et prétraitement de la page X/N » affiché dans la barre de progression.
  Contrairement aux images, le nettoyage complet (deskew/despeckle/flou+
  netteté) n'est **pas** appliqué ici : testé et écarté après avoir constaté
  qu'il déclenche un bug interne d'Audiveris sur certaines partitions
  (export corrompu → échec d'import MuseScore 4) sans bénéfice sur une page
  déjà rendue numériquement.
- Téléchargement à la demande des langues OCR Tesseract : le sélecteur
  propose désormais, en plus des langues déjà installées dans Audiveris,
  l'ensemble des langues du dépôt GitHub `tesseract-ocr/tessdata` (~130,
  catalogue mis en cache localement pour rester disponible hors connexion).
  Une langue marquée « à télécharger » est récupérée automatiquement au
  lancement de la conversion, avant l'appel à Audiveris.
- Sélecteur de langue d'OCR : les ~130 langues du dépôt `tesseract-ocr/tessdata`
  s'affichent désormais avec leur nom complet en français (ex. « Gaélique
  écossais ») au lieu du code Tesseract à 3 lettres (« GLA »), pour rester
  lisible au-delà des quelques langues les plus courantes.
- Le fichier source accepte désormais aussi un projet Audiveris (`.omr`)
  directement, en plus d'une image ou d'un PDF : permet de reprendre un
  `.omr` corrigé à la main dans l'interface d'Audiveris (après une première
  conversion avec « Conserver le projet ») et de l'exporter vers `.mscz`
  sans repasser par la reconnaissance. Aucun prétraitement n'est appliqué
  dans ce cas, le fichier est transmis tel quel à Audiveris.
- Fusion des mouvements : les parties vocales sont désormais toujours
  replacées au-dessus des parties instrumentales dans le fichier fusionné
  (convention d'écriture standard), même si Audiveris les détecte après
  l'accompagnement (ex. piano seul en intro). Détection basée en priorité
  sur le patch GM « Voice Oohs » qu'Audiveris assigne systématiquement aux
  portées vocales, avec un repli sur le nom de la partie (S./A./T./B.,
  Soprano, Voix...) puis sur la présence de paroles (`<lyric>`).
- Mise à jour automatique d'Audiveris/MuseScore via `winget upgrade --silent`
  quand la vérification de version au démarrage détecte un retard sur la
  dernière version publiée. Tourne en arrière-plan (l'appli reste utilisable
  immédiatement) ; une seule invite UAC apparaît même si les deux outils sont
  concernés ; la zone « Versions détectées » affiche « mise à jour en
  cours... » pendant l'opération. L'appli ne télécharge/installe jamais rien
  elle-même : tout passe par winget.
- Installation automatique (et plus seulement mise à jour) d'Audiveris/
  MuseScore via `winget install --silent` quand l'un des deux outils est
  détecté absent au démarrage (typiquement juste après l'installation du
  nouvel installateur `Setup.msi`, ci-dessous) — même mécanisme d'élévation
  unique que la mise à jour (`ToolUpdater.TryApplyAsync`).
- Icône de l'application (note de musique sur portée, dégradé indigo/violet) :
  source vectorielle `assets/icon/icon.svg`, script `assets/icon/generate_ico.py`
  produisant un `.ico` multi-résolution (16 à 256 px), utilisée à la fois pour
  l'exécutable (`<ApplicationIcon>`) et la fenêtre (`Icon=` dans
  `MainWindow.xaml`).
- Installateur Windows `Setup.msi` (`scripts/build-installer.ps1`) : publie
  l'application en self-contained single-file (runtime .NET et dépendances
  natives embarqués, aucune dépendance séparée à installer) puis génère le
  `.msi` avec WiX v5 (`installer/Package.wxs`) — installe l'exécutable, un
  raccourci dans le menu Démarrer et une entrée Ajout/Suppression de
  programmes ; installation/désinstallation silencieuses vérifiées de bout en
  bout. L'installation d'Audiveris/MuseScore eux-mêmes reste volontairement
  hors du `.msi` (voir ci-dessus) plutôt qu'en custom action, `winget` n'étant
  pas fiable sous le compte SYSTEM qu'utilisent les actions différées d'un
  `.msi` par-machine élevé.
- Écran de premier lancement bloquant lorsqu'Audiveris et/ou MuseScore sont
  détectés absents : le formulaire de conversion (inutilisable sans ces
  outils) est masqué au profit d'un écran d'attente explicatif pendant
  l'installation automatique via winget, puis restauré une fois terminée.
- Icône animée du spinner (notes de musique ↔ chiffres binaires : ♪, 01, ♫,
  10...) affichée pendant la conversion et l'écran de premier lancement, pour
  évoquer ce que fait le pipeline (partition → données binaires → score
  numérique) plutôt qu'une simple rotation générique.

### Fixed
- OCR : la variante `tessdata_best` (LSTM seul) est incompatible avec le
  moteur Tesseract *legacy* requis par Audiveris — elle fait échouer toute
  reconnaissance de texte/paroles sans message clair côté application
  (`Could not initialize TessBaseAPI languages: ... in legacy mode`, aucun
  texte produit). Le téléchargement à la demande (ci-dessus) cible
  exclusivement le dépôt `tesseract-ocr/tessdata` principal pour cette
  raison ; ne jamais y substituer `tessdata_fast`/`tessdata_best`.
- `.omr` direct en entrée : Audiveris ignore l'option `-output` quand le
  fichier source est un `.omr` déjà existant et réexporte le `.mxl` à côté
  de son emplacement courant — sans correction, la fusion ne trouvait aucun
  fichier MusicXML dans le dossier de travail temporaire. Le `.omr` source
  est désormais copié dans ce dossier avant l'appel à Audiveris.
- Fusion des mouvements : les parties étaient associées par leur seul nom,
  ce qui fusionnait à tort deux instruments différents portant le même nom
  générique (ex. deux voix nommées « Voice » quand l'OCR ne peut pas lire
  leurs abréviations faute de langue installée) — désynchronisait les
  mesures et faisait échouer l'export MuseScore 4. La correspondance utilise
  désormais une clé par occurrence (`Voice#2`, `Voice#3`...) en plus du nom.

### Changed
- Pivot de l'architecture vers C#/.NET (WPF), après des prototypes PowerShell
  puis Rust/eframe abandonnés en cours de route.
- Réorganisation interne du code : `MainWindow.xaml.cs` (823 lignes) scindé en
  cinq classes dédiées (`ProcessRunner`, `ToolLocator`, `ToolVersionChecker`,
  `MusicXmlMerger`, `ScoreConverter`) ; `MainWindow.xaml.cs` ne garde que les
  handlers UI. Aucun changement de comportement.
- L'exécutable et le `.msi` ne sont pas signés (pas de certificat
  Authenticode) : Windows SmartScreen affiche un avertissement à
  l'installation/au premier lancement. Accepté pour l'instant (projet à usage
  personnel + petit dépôt public) plutôt que de payer un certificat de
  signature de code ; documenté dans le README.
