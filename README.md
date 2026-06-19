# partition2musescore

*Convertisseur partition (PDF/image) → MuseScore, **100 % local** : reconnaissance optique de musique via Audiveris, export final via MuseScore 4 — sans cloud, sans clé API.*

![.NET](https://img.shields.io/badge/.NET-10-blue)
![WPF](https://img.shields.io/badge/UI-WPF-blue)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](LICENSE.md)

Convertit une partition scannée ou photographiée (PDF ou image) en fichier MuseScore (`.mscz`), prêt à être ouvert et édité dans MuseScore Studio 4.

- **Reconnaissance optique de musique (OMR)** via [Audiveris](https://github.com/Audiveris/audiveris) — moteur Java open source, exécuté en local.
- **Export final** via la CLI de MuseScore Studio 4 — aucune réimplémentation du format `.mscz`/MusicXML.
- **Fusion automatique des mouvements** : Audiveris découpe parfois une partition en plusieurs morceaux (ex. une intro instrumentale seule, puis des voix qui entrent ensuite) — l'application les recolle en un seul fichier continu, silences comblés pour garder toutes les parties synchronisées.
- **Détection automatique** des installations d'Audiveris et MuseScore 4 (registre Windows), quel que soit le disque d'installation.
- **Barre de progression réelle**, basée sur les logs d'Audiveris (étape et page en cours), pas une animation.
- **Journal d'erreur** écrit automatiquement à côté de la destination si la conversion échoue.

---

## Prérequis

- Windows (application WPF)
- [.NET 10 SDK](https://dotnet.microsoft.com/) pour compiler/exécuter
- [Audiveris](https://github.com/Audiveris/audiveris/releases) (testé avec la 5.10.2) installé localement
- [MuseScore Studio 4](https://musescore.org/) installé localement

L'application détecte Audiveris et MuseScore 4 via le registre Windows (`InstallLocation`) quel que soit leur emplacement d'installation ; à défaut, elle se replie sur `C:\Program Files\Audiveris\Audiveris.exe` et `C:\Program Files\MuseScore 4\bin\MuseScore4.exe`.

---

## Installation

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

1. **Fichier source** — *Parcourir...* pour choisir un PDF ou une image (PDF, JPG, PNG, TIFF) de la partition à convertir.
2. **Fichier de destination** — pré-rempli automatiquement (même nom, extension `.mscz`) dès que le fichier source est choisi ; modifiable via *Parcourir...*.
3. **Convertir la partition** — lance le pipeline ; la barre de progression affiche l'étape Audiveris en cours et la page traitée.
4. En fin de conversion, un bouton permet d'ouvrir directement le dossier de destination dans l'Explorateur.

En cas d'échec, un fichier `<destination>_erreur_<horodatage>.log` est créé à côté de la destination prévue, avec le détail de l'erreur et tout ce qu'Audiveris/MuseScore 4 ont affiché.

---

## Pipeline de conversion

```
Image/PDF
   │
   ▼
Audiveris CLI (-batch -export)        — reconnaissance optique de musique
   │  → un .mxl (MusicXML) par "mouvement" détecté
   ▼
Fusion des mouvements                  — association des parties par nom,
   │                                      mesures de silence pour les parties
   │                                      absentes d'un mouvement
   ▼
MuseScore 4 CLI (-o sortie.mscz)       — export final
   │
   ▼
.mscz
```

Voir [`CLAUDE.md`](CLAUDE.md) pour le détail technique de chaque étape.

---

## Limitations connues

Héritées du moteur Audiveris (voir [memory/audiveris_handbook_reference.md](memory/audiveris_handbook_reference.md)) :

- Hampes opposées très proches : peuvent être fusionnées en une seule, correction manuelle parfois nécessaire.
- Tuplets : seuls triolets et sextolets sont reconnus.
- Qualité de numérisation recommandée : niveaux de gris, 300 DPI (400 pour petits symboles), sans rotation/voilure.

---

## Développement

```powershell
dotnet build Partition2MuseScore.slnx
```

Pas encore de projet de tests ni d'analyseurs configurés — voir [`CONTRIBUTING.md`](CONTRIBUTING.md) pour l'état courant et les conventions du projet.

## Licence

[AGPL v3](LICENSE.md)
