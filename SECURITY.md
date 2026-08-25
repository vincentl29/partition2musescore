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

partition2musescore est une application desktop **C#/.NET (WPF)** qui convertit
des fichiers fournis par l'utilisateur (images scannées, PDF de partitions) en
fichiers MuseScore (`.mscz`), en pilotant deux outils externes déjà installés
sur la machine : **Audiveris** (reconnaissance optique de musique) et
**MuseScore 4** (export final), tous deux invoqués via `System.Diagnostics.Process`.
Le projet lui-même n'embarque aucune dépendance NuGet à ce jour ; les vecteurs
d'attaque les plus pertinents sont les suivants.

**Fichiers d'entrée malveillants** : une image ou un PDF spécialement conçu
pourrait exploiter une vulnérabilité dans Audiveris (décodage d'image/PDF,
OCR Tesseract) plutôt que dans le code de partition2musescore lui-même — le
fichier ne passe jamais par un parseur d'image/PDF en mémoire dans l'appli.
Ne traitez pas de fichiers provenant de sources non fiables sans précautions
(bac à sable, VM).

**Outils externes locaux** : Audiveris et MuseScore 4 ne sont pas embarqués ni
téléchargés par partition2musescore — l'appli se contente de les localiser via
le registre Windows (clé `Uninstall`, `InstallLocation`) puis de les lancer.
Un de ces deux outils compromis ou trafiqué sur la machine de l'utilisateur
s'exécutera avec les mêmes privilèges que l'appli ; partition2musescore ne
vérifie pas leur intégrité (signature, somme de contrôle) avant de les lancer.

**Élévation de privilèges via winget** : si Audiveris/MuseScore sont absents
ou obsolètes, l'appli lance un processus PowerShell **élevé** (invite UAC) qui
exécute `winget install`/`winget upgrade --silent` pour le(s) paquet(s)
concerné(s) (`audiveris.org.Audiveris` / `Musescore.Musescore`). L'appli
elle-même ne télécharge ni n'installe jamais rien directement — elle délègue
entièrement à `winget`, le gestionnaire de paquets officiel de Windows — mais
cela reste une élévation de privilèges déclenchée automatiquement au
démarrage ; un utilisateur qui refuse l'invite UAC continue simplement avec la
version actuellement installée (ou sans l'outil, s'il est absent).

**Construction de la ligne de commande** : les chemins de fichiers
(source, dossier de travail temporaire, destination) sont actuellement
insérés par interpolation de chaîne dans les arguments passés à
`Audiveris.exe`/`MuseScore4.exe`, entourés de guillemets. Un chemin contenant
lui-même un caractère `"` pourrait théoriquement rompre ce guillemetage et
injecter un argument supplémentaire dans la ligne de commande de l'outil
externe. Une migration vers `ProcessStartInfo.ArgumentList` (qui échappe
chaque argument indépendamment) éliminerait ce risque.

**Chemins de sortie** : le chemin de destination est choisi par l'utilisateur
via un sélecteur de fichier natif (pas de saisie libre arbitraire d'un nom
puis concaténation), ce qui limite le risque de traversée de répertoire
(`../`) — mais toute évolution qui réintroduirait une concaténation de chemin
à partir d'une chaîne non validée devrait être examinée avec attention.

**Appel réseau sortant** : au démarrage, l'application interroge l'API
publique GitHub (`api.github.com`, HTTPS) pour connaître la dernière version
publiée d'Audiveris et de MuseScore, à titre informatif uniquement — aucun
binaire n'est téléchargé ni exécuté à partir de cette réponse. La dernière
version connue est mise en cache localement
(`%LOCALAPPDATA%\Partition2MuseScore\version_cache.json`) ; un échec de cette
requête (pas de connexion) n'empêche pas la conversion de fonctionner.

**Fichiers `.mscz` générés** : ce sont des archives zip contenant du XML,
produites entièrement par `MuseScore4.exe` — partition2musescore ne génère
lui-même que le MusicXML intermédiaire (fusion des mouvements détectés par
Audiveris), jamais directement le zip `.mscz`.

**Installateur `Setup.msi`** : généré par `scripts/build-installer.ps1` (publication
self-contained + `wix build`), il installe l'application par-machine (Program
Files), ce qui nécessite une élévation à l'installation comme à la
désinstallation — comportement standard pour tout `.msi` par-machine, sans
spécificité supplémentaire de partition2musescore. L'installation
d'Audiveris/MuseScore eux-mêmes n'est volontairement **pas** réalisée par une
custom action du `.msi` : elle est déléguée à l'appli au
premier lancement (paragraphe ci-dessus), dans le contexte utilisateur
interactif plutôt que sous le compte SYSTEM qu'utilisent les actions
différées d'un `.msi` élevé.

## Bonnes pratiques recommandées

- Exécutez partition2musescore dans un environnement isolé (conteneur, VM) si
  vous traitez des fichiers de sources inconnues.
- Épinglez les versions des dépendances et vérifiez régulièrement les mises à
  jour de sécurité.
- Ne lancez pas le programme avec des privilèges élevés.
