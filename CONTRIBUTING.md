# Contribuer à partition2musescore

Ce projet est développé et maintenu par une seule personne. Les contributions
externes sont les bienvenues dans la mesure du possible, mais les réponses et
les décisions de merge restent à la discrétion du mainteneur.

La langue de travail du projet est le **français** (issues, PR, commits).

---

## État du projet

Le projet est encore au stade de l'amorçage : le pipeline de conversion
(image/PDF → reconnaissance optique de musique → fichier MuseScore) n'est pas
encore implémenté. La section *Lancer le projet / Tests et qualité* ci-dessous
sera complétée dès qu'un manifeste de paquet (ex. `pyproject.toml`) existera —
voir `CLAUDE.md` pour l'état courant et les choix d'architecture déjà actés.

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
2. Tests et lint verts (une fois le projet outillé — voir `CLAUDE.md`).
3. Si l'architecture change, **mettre à jour `CLAUDE.md` et `README.md`** en conséquence.
4. Ajouter une entrée sous `## [Unreleased]` dans `CHANGELOG.md`.
5. Décrire le *pourquoi* du changement dans la PR.

## Style de code

- Les commentaires/docstrings expliquent **pourquoi** une chose est faite, pas
  seulement quoi — documenter les pièges et décisions non évidentes (ex. choix
  d'un moteur OMR, gestion de versions du format MuseScore…).
- Privilégier les fonctions pures et testables ; isoler les effets de bord
  (I/O fichier, modèles ML) dans des modules dédiés.

## Repères d'architecture

Voir [`CLAUDE.md`](./CLAUDE.md) — c'est la source de vérité pour la structure du
code, les commandes et les décisions de conception au fur et à mesure qu'elles
sont prises.
