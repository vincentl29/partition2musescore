# Contribuer à partition2musescore

Ce projet est développé et maintenu par une seule personne. Les contributions
externes sont les bienvenues dans la mesure du possible, mais les réponses et
les décisions de merge restent à la discrétion du mainteneur.

La langue de travail du projet est le **français** (issues, PR, commits).

---

## État du projet

Application desktop **C#/.NET 10 (WPF)** — solution `Partition2MuseScore.slnx`,
code dans `src/Partition2MuseScore/`. `MainWindow.xaml.cs` ne contient que les
handlers UI ; le pipeline de conversion (image/PDF → Audiveris → fusion
MusicXML → MuseScore 4 CLI → `.mscz`) est implémenté dans des classes dédiées
(`ScoreConverter`, `MusicXmlMerger`, `ToolLocator`, `ToolVersionChecker`,
`ProcessRunner`) ; voir `CLAUDE.md` pour le détail de ce qui est fait.

## Lancer le projet

```powershell
dotnet build Partition2MuseScore.slnx     # compiler
dotnet run --project src/Partition2MuseScore   # lancer l'appli
```

Nécessite le SDK .NET 10 et Windows (WPF). Pour une conversion réelle,
Audiveris et MuseScore 4 doivent être installés (détectés via le registre
Windows, indépendamment du lecteur d'installation) ; l'appli les installe
elle-même via `winget` au premier lancement si l'un des deux est absent.

Pour générer l'installateur Windows (`Setup.msi`) :

```powershell
pwsh scripts/build-installer.ps1
```

Nécessite en plus le CLI WiX v5 (`dotnet tool install --global wix --version 5.0.2`
— pas v6/v7, voir `CLAUDE.md`). Voir [`CLAUDE.md`](CLAUDE.md), section
« Installateur Windows (Setup.msi) », pour le détail.

## Tests et qualité

Pas encore de projet de tests ni d'analyseurs configurés au-delà des
diagnostics par défaut du SDK — `dotnet build` sans avertissement est pour
l'instant le seul filet de sécurité avant une PR.

## Conventions de commit

Le projet suit les [Conventional Commits](https://www.conventionalcommits.org/),
**rédigés en français** :

```
feat: ajout du routeur OMR page par page
fix: chemin de sortie non assaini sur Windows
docs: mise à jour README.md
refactor: …      test: …      chore: …
```

## Pull requests

1. Créer une branche dédiée depuis la branche par défaut.
2. `dotnet build Partition2MuseScore.slnx` sans avertissement (voir *Tests et qualité*).
3. Si l'architecture change, **mettre à jour `CLAUDE.md` et `README.md`** en conséquence.
4. Ajouter une entrée sous `## [Unreleased]` dans `CHANGELOG.md`.
5. Décrire le *pourquoi* du changement dans la PR.

## Style de code

- Les commentaires/docstrings expliquent **pourquoi** une chose est faite, pas
  seulement quoi — documenter les pièges et décisions non évidentes (ex. choix
  d'un moteur OMR, gestion de versions du format MuseScore…).
- Privilégier les fonctions pures et testables ; isoler les effets de bord
  (I/O fichier, appels aux outils externes Audiveris/MuseScore 4) dans des
  méthodes dédiées plutôt que les mêler à la logique de fusion/parsing.

## Repères d'architecture

Voir [`CLAUDE.md`](./CLAUDE.md) — c'est la source de vérité pour la structure du
code, les commandes et les décisions de conception au fur et à mesure qu'elles
sont prises.
