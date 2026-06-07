# Politique de sécurité

## Avertissement

partition2musescore est un projet **open source en cours de développement**, fourni
tel quel, sans garantie d'aucune sorte. Le mainteneur ne peut être tenu responsable
de tout dysfonctionnement, perte de données, faille de sécurité ou tout autre
problème découlant de l'utilisation de ce logiciel, que ce soit directement ou
indirectement. L'utilisation se fait à vos propres risques.

## Versions prises en charge

Seule la dernière version de la branche par défaut reçoit des correctifs de
sécurité. Les versions antérieures ne sont pas maintenues.

| Version                | Prise en charge |
|------------------------|---|
| Branche par défaut     | ✅ |
| Antérieures            | ❌ |

## Signaler une vulnérabilité

**Merci de ne pas ouvrir d'issue publique pour une faille de sécurité.**

Utilisez l'onglet **Security → Report a vulnerability** de GitHub pour un
signalement confidentiel, ou écrivez à **partition2musescore@leopold.bzh**.
Merci d'inclure :

- une description du problème et de son impact potentiel ;
- les étapes de reproduction (fichier d'exemple, commande, configuration) ;
- la version / le commit concerné.

Ce projet est maintenu bénévolement, dans le temps disponible. Les signalements
seront traités au mieux, sans engagement de délai.

## Périmètre

partition2musescore convertit des fichiers fournis par l'utilisateur (images
scannées, PDF de partitions) en fichiers MuseScore (`.mscz`/`.mscx`), via un
moteur de reconnaissance optique de musique (OMR) et potentiellement des modèles
téléchargés depuis des sources tierces. Les vecteurs d'attaque les plus
pertinents sont les suivants.

**Fichiers d'entrée malveillants** : une image ou un PDF spécialement conçu
pourrait exploiter une vulnérabilité dans les bibliothèques de décodage
d'image/PDF ou dans le moteur OMR. Ne traitez pas de fichiers provenant de
sources non fiables sans précautions.

**Intégrité des modèles téléchargés** : si le moteur OMR repose sur des modèles
de machine learning téléchargés au premier lancement (HuggingFace ou autre),
aucune vérification cryptographique de leur intégrité n'est garantie par le
projet ; vous dépendez de la sécurité de la chaîne d'approvisionnement amont.

**Exécution de code via les dépendances** : le projet s'appuie sur des
bibliothèques de traitement d'image/PDF et potentiellement des frameworks de
ML lourds. Une dépendance compromise ou mal épinglée pourrait introduire du
code malveillant lors de l'installation des dépendances.

**Chemins de sortie** : les noms de fichiers d'entrée influencent les noms des
fichiers `.mscz`/`.mscx` générés. Un nom de fichier contenant des séquences de
traversée de répertoire (ex. `../`) pourrait écrire hors du répertoire de
sortie prévu si l'entrée n'est pas correctement assainie.

**Fichiers `.mscz`/`.mscx` générés** : ce sont des archives zip contenant du
XML. Une génération incorrecte ou une dépendance à un outil tiers pour
l'assemblage final (ex. appel à MuseScore en ligne de commande) doit éviter
toute injection de commande basée sur des noms/chemins fournis par l'utilisateur.

## Bonnes pratiques recommandées

- Exécutez partition2musescore dans un environnement isolé (conteneur, VM) si
  vous traitez des fichiers de sources inconnues.
- Épinglez les versions des dépendances et vérifiez régulièrement les mises à
  jour de sécurité.
- Ne lancez pas le programme avec des privilèges élevés.
