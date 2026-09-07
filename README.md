# Aeropeek

Un outil de réglage pour Windows, orienté CS2, qui **mesure au lieu de promettre**.

La plupart des « optimisations » qui circulent ne sont jamais vérifiées. Aeropeek
part du principe inverse : il te dit quand un réglage ne sert à rien, il note
tout ce qu'il modifie avec l'état exact d'avant, et il sait tout remettre en
place.

## Ce qu'il fait

- **Diagnostic** — une douzaine de vérifications sur la machine : plan
  d'alimentation, sortie vidéo, âge du pilote graphique, topologie du
  processeur, surcouches, hyperviseur. Il ne modifie rien.
- **Réglages** — un catalogue de modifications du registre, chacune avec son
  gain attendu, sa contrepartie, et son état réel sur cette machine.
- **Mode Match** — suspend les services et ferme les applications qui peuvent
  interrompre une partie, puis remet tout en état à la fin.
- **Nettoyage** — caches de shaders, fichiers temporaires, rapports d'erreur.
- **Benchmark** — capture les temps d'image via PresentMon et compare deux
  mesures pour dire si un réglage a servi.
- **DNS**, **Utilitaires**, **Restauration**.

## Ce qu'il ne fait pas

Ce sont des interdits inscrits dans le code, pas de simples recommandations :

- il ne désactive pas l'antivirus ni les services de sécurité ;
- il ne touche pas à Windows Update ;
- il ne supprime aucun fichier système ;
- il ne touche pas aux services d'anticheat (FACEIT, Vanguard, EAC, BattlEye) ;
- il n'envoie rien sur Internet et ne collecte aucune donnée. Les seules
  connexions sortantes sont facultatives : vérifier la dernière version du
  pilote NVIDIA, et télécharger un utilitaire tiers si tu le demandes.

Tout ce qu'il écrit reste dans `%LOCALAPPDATA%\Aeropeek`.

## Annuler

Chaque modification est journalisée **avant** d'être appliquée, avec la valeur
précédente et le fait qu'elle existait ou non. L'onglet Restauration remet tout
en état, réglage par réglage ou d'un bloc. Si l'application se ferme sans
terminer proprement une session, elle le détecte au lancement suivant et propose
d'annuler ce qui restait appliqué.

Une modification faite **avant** qu'Aeropeek ne la voie n'est pas annulable : le
programme ne restaure que ce qu'il a lui-même écrit, et il le dit clairement
plutôt que d'inventer une valeur par défaut.

## Prérequis

- Windows 10 build 17763 ou plus récent — Windows 11 recommandé
- [.NET 10 SDK](https://dotnet.microsoft.com/download) pour compiler
- Droits administrateur à l'exécution : le programme lit et modifie des réglages
  système, c'est inscrit dans son manifeste.

## Compiler et lancer

```
git clone <url-du-depot>
cd Aeropeek
dotnet run -c Release
```

Pour produire une version distribuable :

```
dotnet publish -c Release -o dist
```

## Ajouter un réglage

Le catalogue est un simple fichier JSON, [`catalogue.json`](catalogue.json) — pas
besoin de recompiler pour en ajouter un :

```json
{
  "id": "mon-reglage",
  "nom": "Titre affiché",
  "explication": "Ce que ça fait, en une phrase.",
  "consequence": "Ce que l'utilisateur perd en échange.",
  "gain": "jusqu'à +8 % sur les 1% lows",
  "categorie": "performance",
  "redemarrage": false,
  "applicable": { "buildMin": 19041, "gpu": "nvidia", "portable": "non" },
  "operations": [
    {
      "hive": "HKCU",
      "cle": "Software\\Exemple",
      "valeur": "NomDeLaValeur",
      "type": "dword",
      "vers": 0
    }
  ]
}
```

`applicable` est facultatif. Chacun de ses champs masque le réglage — avec sa
raison affichée — sur les machines qui ne sont pas concernées : `buildMin` et
`buildMax` pour la version de Windows, `gpu` pour le fabricant de la carte
graphique, `portable: "non"` pour l'exclure des ordinateurs portables.

Certains chemins du registre sont refusés quelle que soit la provenance du
catalogue : Windows Defender, les services de sécurité, `SAM`, `SECURITY` et
`Control\Lsa`. Un réglage qui les viserait s'afficherait « Bloqué ».

## Avertissement

Ce programme modifie des réglages système. Il journalise tout et sait revenir en
arrière, mais aucun outil ne remplace une sauvegarde. Crée un point de
restauration avant une première utilisation — l'onglet Restauration le fait pour
toi.

## Licence

[Apache-2.0](LICENSE). Voir [NOTICE](NOTICE) et
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) pour les composants tiers.

Aeropeek redistribue [Intel PresentMon](https://github.com/GameTechDev/PresentMon)
(licence MIT) pour la capture des temps d'image.
