# partition2musescore

*Convertisseur partition (PDF/image) → MuseScore, **100 % local** : reconnaissance optique de musique via Audiveris, export final via MuseScore 4 — sans cloud, sans clé API.*

![.NET](https://img.shields.io/badge/.NET-10-blue)
![WPF](https://img.shields.io/badge/UI-WPF-blue)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE.md)
[![Release](https://img.shields.io/github/v/release/vincentl29/partition2musescore)](https://github.com/vincentl29/partition2musescore/releases/latest)

Convertit une partition scannée ou photographiée (PDF ou image) en fichier MuseScore (`.mscz`), prêt à être ouvert et édité dans MuseScore Studio 4.

- **Prétraitement avant Audiveris** : pour une image (JPG/PNG/TIFF/BMP), niveaux de gris, redressement, débruitage et net renforcé, pour de meilleurs résultats sur des scans/photos moyens. Pour un PDF, chaque page est extraite en PNG à 300 DPI, convertie en niveaux de gris, puis réassemblée en un nouveau PDF transmis à Audiveris (sans le redressement/débruitage : inutile, voire nuisible, sur une page déjà rendue numériquement).
- **Reconnaissance optique de musique (OMR)** via [Audiveris](https://github.com/Audiveris/audiveris) — moteur Java open source, exécuté en local.
- **Export final** via la CLI de MuseScore Studio 4 — aucune réimplémentation du format `.mscz`/MusicXML.
- **Langue d'OCR configurable** (texte/paroles) parmi les langues déjà installées dans Audiveris et, au-delà, l'ensemble des ~130 langues du dépôt Tesseract — celles non encore installées sont téléchargées automatiquement à la sélection.
- **Fusion automatique des mouvements** : Audiveris découpe parfois une partition en plusieurs morceaux (ex. une intro instrumentale seule, puis des voix qui entrent ensuite) — l'application les recolle en un seul fichier continu, silences comblés pour garder toutes les parties synchronisées, même quand deux instruments différents reçoivent le même nom générique.
- **Voix placées au-dessus des instruments** : à l'issue de la fusion, les parties vocales sont systématiquement replacées en tête de partition (convention d'écriture standard), même si Audiveris les a détectées après les instruments d'accompagnement.
- **Conservation optionnelle du projet Audiveris (.omr)** pour correction manuelle avant réexport — ce `.omr` corrigé peut ensuite être réutilisé directement comme fichier source pour ré-exporter vers `.mscz` sans refaire la reconnaissance.
- **Style MuseScore optionnel (.mss)** appliqué à l'export final.
- **Détection automatique** des installations d'Audiveris et MuseScore 4 (registre Windows), quel que soit le disque d'installation.
- **Affichage des versions, installation et mise à jour automatiques** : au démarrage, la version installée de chaque outil (registre Windows) et la dernière version disponible (dépôts GitHub officiels) sont affichées côte à côte ; la dernière version connue est mise en cache localement pour rester affichée même sans connexion internet. Si l'un des deux outils est absent (par exemple juste après l'installation du `Setup.msi`, sur un PC neuf) ou a du retard, une installation/mise à jour silencieuse via `winget` est lancée en arrière-plan (une seule invite UAC même si les deux outils sont concernés) ; l'application ne télécharge ni n'installe jamais rien elle-même, tout passe par winget.
- **Barre de progression réelle**, basée sur les logs d'Audiveris (étape et page en cours), pas une animation.
- **Nettoyage préventif du MusicXML** : avant de passer le fichier à MuseScore, les crescendos/decrescendos (`<wedge>`) que Audiveris n'a pas pu positionner (mesures marquées « no correct rhythm ») sont supprimés automatiquement — ces données incomplètes feraient rejeter le fichier par MuseScore. Les nuances perdues peuvent être rajoutées manuellement dans MuseScore.
- **Journal d'erreur** écrit automatiquement à côté de la destination si la conversion échoue ; nouvel essai automatique en cas d'échec ponctuel de l'export MuseScore. En cas d'erreur 1320 persistante, un message guidé indique comment corriger le projet Audiveris (`.omr`) à la main.
- **Arrêt propre** des process Audiveris/MuseScore 4 en cours si la fenêtre est fermée pendant une conversion.

---

## Prérequis

- Windows (application WPF)
- [.NET 10 SDK](https://dotnet.microsoft.com/) — uniquement pour compiler ou générer `Setup.msi` ; le `.msi` une fois construit s'installe sur une machine sans SDK ni runtime .NET (publication self-contained)
- [Audiveris](https://github.com/Audiveris/audiveris/releases) (testé avec la 5.10.2) et [MuseScore Studio 4](https://musescore.org/) — installés automatiquement via `winget` au premier lancement s'ils sont absents (voir ci-dessous), sinon installables manuellement

L'application détecte Audiveris et MuseScore 4 via le registre Windows (`InstallLocation`) quel que soit leur emplacement d'installation ; à défaut, elle se replie sur `C:\Program Files\Audiveris\Audiveris.exe` et `C:\Program Files\MuseScore 4\bin\MuseScore4.exe`.

L'installation/mise à jour automatique au démarrage (ci-dessus) nécessite `winget` (App Installer, préinstallé sur Windows 10 1809+/Windows 11) ; les paquets utilisés sont `audiveris.org.Audiveris` / `Musescore.Musescore` — sans `winget` disponible, l'opération échoue silencieusement et l'application continue avec la version actuellement installée (ou sans l'outil, s'il est absent).

**Langues OCR** : le sélecteur propose les langues déjà installées dans Audiveris (dossier `%APPDATA%\AudiverisLtd\audiveris\config\tessdata\`) ainsi que toutes celles du dépôt [`tesseract-ocr/tessdata`](https://github.com/tesseract-ocr/tessdata) — ces dernières marquées « à télécharger » sont récupérées automatiquement au lancement de la conversion. N'utilisez pas les variantes [`tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast)/[`tessdata_best`](https://github.com/tesseract-ocr/tessdata_best) : elles sont LSTM seul, alors qu'Audiveris exige le moteur Tesseract *legacy* — les installer fait échouer l'OCR sans aucun message clair (texte/paroles totalement absents du résultat).

---

## Installation

### Utilisateurs

```powershell
pwsh scripts/build-installer.ps1
```

Construit `installer/bin/Partition2MuseScoreSetup.msi` puis l'exécuter (double-clic, ou `msiexec /i installer\bin\Partition2MuseScoreSetup.msi /quiet` pour une installation silencieuse). Le binaire publié est autonome (self-contained) : aucun runtime .NET à installer séparément. Audiveris et MuseScore 4, s'ils sont absents, sont installés automatiquement via `winget` au premier lancement de l'application (voir ci-dessus) ; nécessite le SDK .NET 10 et WiX v5 pour construire l'installateur (voir [Développement](#développement)).

> **Note** : l'exécutable et le `.msi` ne sont pas signés (pas de certificat Authenticode), donc Windows SmartScreen affiche un avertissement « Windows a protégé votre ordinateur » à l'installation/au premier lancement. C'est normal pour un projet distribué sans certificat de signature de code — cliquez sur *Informations complémentaires* puis *Exécuter quand même* pour continuer.

### Développeurs

```powershell
git clone <repo>
cd partition2musescore
dotnet build Partition2MuseScore.slnx
```

---

## Utilisation

```powershell
dotnet run --project src/Partition2MuseScore
```

1. **Fichier source** — *Parcourir...* pour choisir un PDF ou une image (PDF, JPG, PNG, TIFF) de la partition à convertir, ou un projet Audiveris (`.omr`) déjà généré (cf. étape 4) pour reprendre une correction manuelle sans refaire la reconnaissance.
2. **Fichier de destination** — pré-rempli automatiquement (même nom, extension `.mscz`) dès que le fichier source est choisi ; modifiable via *Parcourir...*.
3. **Langue de l'OCR** — langue dominante du texte/des paroles, pour une meilleure reconnaissance (le paquet Tesseract correspondant doit être installé dans Audiveris).
4. *(optionnel)* **Conserver le projet Audiveris (.omr)** — coche cette case pour pouvoir rouvrir la reconnaissance dans Audiveris et corriger manuellement avant de réexporter.
5. *(optionnel)* **Style MuseScore** — fichier `.mss` à appliquer à l'export final.
6. **Convertir la partition** — lance le pipeline ; la barre de progression affiche l'étape Audiveris en cours et la page traitée.
7. En fin de conversion, un bouton permet d'ouvrir directement le dossier de destination dans l'Explorateur.

En cas d'échec, un fichier `<destination>_erreur_<horodatage>.log` est créé à côté de la destination prévue, avec le détail de l'erreur et tout ce qu'Audiveris/MuseScore 4 ont affiché. Fermer la fenêtre pendant qu'une conversion est en cours demande confirmation avant d'arrêter Audiveris/MuseScore 4.

---

## Pipeline de conversion

```
Image/PDF
   │
   ▼
Prétraitement                          — image : niveaux de gris, redressement,
   │                                      débruitage, net renforcé (Magick.NET)
   │                                    PDF : rendu page par page à 300 DPI,
   │                                      niveaux de gris, réassemblage PDF
   ▼
Audiveris CLI (-batch -export)        — reconnaissance optique de musique
   │  → un .mxl (MusicXML) par "mouvement" détecté
   ▼
Fusion des mouvements                  — association des parties par nom,
   │                                      mesures de silence pour les parties
   │                                      absentes d'un mouvement, voix
   │                                      replacées au-dessus des instruments
   ▼
Nettoyage MusicXML                     — suppression des crescendos/decrescendos
   │                                      sans position résolue (bug Audiveris
   │                                      sur mesures à rythme non reconnu)
   ▼
MuseScore 4 CLI (-o sortie.mscz)       — export final
   │
   ▼
.mscz
```

---

## Limitations connues

Héritées du moteur Audiveris :

- Hampes opposées très proches : peuvent être fusionnées en une seule, correction manuelle parfois nécessaire.
- Tuplets : seuls triolets et sextolets sont reconnus.
- Qualité de numérisation recommandée : niveaux de gris, 300 DPI (400 pour petits symboles), sans rotation/voilure.
- Crescendos/decrescendos : quand Audiveris signale des erreurs rythmiques sur des mesures (« Voice too long », « no correct rhythm »), les nuances dynamiques associées sont supprimées automatiquement du MusicXML avant l'export (bug Audiveris — données de position `null`). Elles peuvent être rajoutées manuellement dans MuseScore. Si l'export échoue quand même (code 1320), utiliser le `.omr` pour corriger dans Audiveris (un message guidé explique la marche à suivre).

---

## Développement

```powershell
dotnet build Partition2MuseScore.slnx
```

Pas encore de projet de tests ni d'analyseurs configurés — voir [`CONTRIBUTING.md`](CONTRIBUTING.md) pour l'état courant et les conventions du projet.

Pour générer l'installateur (`Setup.msi`) :

```powershell
pwsh scripts/build-installer.ps1
```

Nécessite le CLI WiX v5 (`dotnet tool install --global wix --version 5.0.2`) — volontairement pas v6/v7, qui imposent l'acceptation d'une licence "Open Source Maintenance Fee" avant de pouvoir construire quoi que ce soit.

Pour régénérer l'icône de l'application (`Resources/app.ico`) après modification de `assets/icon/icon.svg` :

```powershell
python assets/icon/generate_ico.py
```

Nécessite Inkscape.

## Licence

[AGPL v3](LICENSE.md)
