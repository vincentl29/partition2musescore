# Changelog

Tous les changements notables de ce projet sont consignés dans ce fichier.

Le format s'inspire de [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et le projet vise le [versionnage sémantique](https://semver.org/lang/fr/).

## [Unreleased]

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

### Changed
- Pivot de l'architecture vers C#/.NET (WPF), après des prototypes PowerShell
  puis Rust/eframe abandonnés en cours de route.
