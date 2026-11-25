# Guide d'utilisation

[Accueil](README.md) · **Guide** · [Mode Pull Request](pull-requests.md) · [Référence et dépannage](reference.md) · [English](en/guide.md)

Installation, initialisation, puis le travail au quotidien : features, releases et hotfixes.

**Sommaire**

1. [Installation](#1-installation)
2. [La fenêtre GitFlow](#2-la-fenêtre-gitflow)
3. [Initialiser un dépôt](#3-initialiser-un-dépôt)
4. [Une feature](#4-une-feature)
5. [Commiter](#5-commiter)
6. [Une release](#6-une-release)
7. [Un hotfix](#7-un-hotfix)
8. [Conflits et reprise](#8-conflits-et-reprise)
9. [Paramètres](#9-paramètres)
10. [Les commandes du menu](#10-les-commandes-du-menu)

---

## 1. Installation

1. **Fermez complètement Visual Studio**, toutes les fenêtres. Sinon, l'installateur se ferme sans rien installer.
2. Double-cliquez sur `GitFlow.VSExtension.vsix`. Selon la version de Visual Studio, le *VSIX Installer* puis le *Visual Studio Installer* prennent la main : validez jusqu'au bout.
3. Relancez Visual Studio. Le menu **Extensions > GitFlow** apparaît, et l'extension est listée dans **Extensions > Gérer les extensions > Installées**.

L'équivalent en ligne de commande :

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe" GitFlow.VSExtension.vsix
```

> **Mise à jour** : même procédure avec le nouveau `.vsix`. Pour désinstaller : **Extensions > Gérer les extensions**, puis *Désinstaller*.

## 2. La fenêtre GitFlow

Ouvrez-la par **Extensions > GitFlow > Fenêtre GitFlow**, ou par **Affichage > Autres fenêtres**. Elle s'ouvre en onglet à côté de *Git Changes*. Elle se met à jour toute seule : à l'enregistrement d'un fichier, au changement de branche, à l'ouverture d'une autre solution.

```text
┌─ GitFlow ─────────────────────────────────────────┐
│ MonProjet                  [Paramètres][Actualiser]│
│ Branche   feature/login                            │
│ Type      Feature                                  │
│ État      Modifications non commitées              │
│ Remote    1 commit(s) en avance, 0 en retard       │
│                                                    │
│ Commit                                             │
│ Modifications (2)                                  │
│  M  src/App/Program.cs                             │
│  A  src/App/Login.cs                               │
│ ┌────────────────────────────────────────────────┐ │
│ │ Met à jour 2 fichiers dans src/App             │ │
│ │ Ajoutés (1) : - src/App/Login.cs …             │ │
│ └────────────────────────────────────────────────┘ │
│              [Régénérer le message] [Commiter]     │
│                                                    │
│ Démarrer une branche                               │
│ [Feature ▾] feature/ [____________] [Démarrer]     │
│                                                    │
│ Branches en cours                                  │
│ Features                                           │
│   login (actuelle)            [Pousser][Terminer…] │
│ Releases                                           │
│ Hotfixes                                           │
└────────────────────────────────────────────────────┘
```

Ce que montre la fenêtre :

- **L'état du dépôt** : branche actuelle et son type, modifications non commitées ou merge en cours, commits en avance ou en retard sur origin.
- **Commit** : les fichiers modifiés et un message proposé ([détails](#5-commiter)).
- **Démarrer une branche** : type, préfixe et nom. Pour une release ou un hotfix, la version suivante est pré-remplie.
- **Branches en cours**, groupées par type, avec *Pousser* et *Terminer…* sur chaque ligne. Pas besoin d'être sur une branche pour la terminer.
- **Badges** :
  - *actuelle* : la branche sur laquelle vous êtes ;
  - *déjà mergée* : une branche déjà mergée à la main dans toutes ses cibles.
- Sur une branche `release/`, le rappel « corrections, docs et tâches de release uniquement ».
- La section *Pull requests* apparaît si vous utilisez le [mode Pull Request](pull-requests.md).

## 3. Initialiser un dépôt

Sur un dépôt qui n'a jamais utilisé GitFlow, la fenêtre propose *Initialiser GitFlow…*. L'écran de paramètres s'ouvre, pré-rempli :

- avec la configuration git-flow existante, si le dépôt en a déjà une (elle est reprise telle quelle) ;
- sinon avec vos réglages « Tous mes dépôts » ;
- sinon avec les valeurs standard : `master`, `develop`, `feature/`, `release/`, `hotfix/`, préfixe de tag vide.

À la validation :
- la branche `develop` est créée si elle n'existe pas : depuis `master`, ou depuis `origin/develop` si elle existe sur le serveur ;
- la configuration est écrite dans le dépôt, avec les clés standard `gitflow.*`, compatibles avec les autres outils git-flow.

> ⚠️ **Le dépôt doit avoir au moins un commit.** Sur un dépôt vide, faites d'abord un premier commit sur `master`.

## 4. Une feature

Une feature part de `develop` et y revient. Elle ne touche jamais `master`.

### Démarrer

1. Dans *Démarrer une branche*, choisissez *Feature* et tapez un nom, par exemple `login` ou `ABC-123-login`.
2. *Démarrer* : la branche `feature/login` est créée depuis develop, et vous êtes placé dessus.

L'extension refuse de démarrer, avec un message explicite, dans ces cas :
- modifications non commitées ;
- nom invalide pour git ;
- branche déjà existante, en local ou sur origin ;
- merge en cours.

### Pousser (optionnel)

*Pousser* envoie la branche sur origin (`git push -u origin feature/login`), pour la partager ou la sauvegarder. Seuls les commits partent : pensez à commiter avant.

### Terminer

*Terminer…* ouvre cette boîte :

```text
┌─ Terminer feature/login ───────────────────────────┐
│ Terminer « feature/login » ?                        │
│   • merge --no-ff dans develop                      │
│   • suppression de la branche locale                │
│                                                     │
│ Mode   (•) Merge local   ( ) Pull Request           │
│ [ ] Pousser les branches mergées (et le tag)        │
│ [ ] Supprimer aussi la branche sur origin           │
│                                     [OK] [Annuler]  │
└─────────────────────────────────────────────────────┘
```

En **merge local**, le mode par défaut :

1. merge `--no-ff` de la feature dans develop, pour garder la trace de la branche dans l'historique ;
2. suppression de la branche locale avec `git branch -d`, que git refuse si la branche n'est pas mergée ;
3. en option, push de develop vers origin et suppression de la branche sur origin ;
4. retour sur develop.

Les deux cases peuvent être pré-cochées dans les [paramètres](#9-paramètres).

## 5. Commiter

La section *Commit* de la fenêtre liste les fichiers modifiés : `A` ajouté, `M` modifié, `D` supprimé, `R` renommé. Elle propose un message construit **uniquement à partir de git**, sans IA : les mêmes modifications donnent toujours le même message.

```text
Met à jour 2 fichiers

Ajoutés (1) :
- src/Login.cs
Modifiés (1) :
- README.md (+2 -1)

Réf. : ABC-12
```

La dernière ligne reprend les tickets trouvés dans le nom de branche (`ABC-123`, `#42`).

- **Ce qui est commité** : si des fichiers sont déjà stagés (`git add`), seulement eux. Sinon tout, nouveaux fichiers compris, comme « Commit All ».
- **Le message reste modifiable** : dès que vous le modifiez, il n'est plus remplacé. *Régénérer le message* remet la proposition.
- **Réécrivez la première ligne** pour les gros commits : le générateur sait *quels* fichiers ont changé, pas *pourquoi*.
- **Enregistrez vos fichiers** (<kbd>Ctrl</kbd>+<kbd>S</kbd>) pour qu'ils apparaissent : git ne voit pas une modification encore dans l'éditeur.

> ⚠️ **Commits refusés** :
> - sur `master` : pas de commit direct en production, démarrez une feature ou un hotfix ;
> - pendant un merge en conflit : commiter terminerait le merge à la place du Finish.

## 6. Une release

Quand assez de features sont dans develop, une release les prépare pour la production. Sur la branche de release : **corrections, documentation et tâches de release uniquement**, plus de nouvelles features.

1. *Démarrer une branche* > *Release* : la version suivante est proposée en incrémentant le *minor* du dernier tag de master (`1.4.1` → `1.5.0`). Vous pouvez la modifier.
2. Si develop ne contient rien de nouveau depuis la dernière release, un avertissement s'affiche : la release serait identique à la précédente. Vous pouvez continuer quand même.
3. Faites vos derniers ajustements (numéro de version, changelog…) et commitez.
4. *Terminer…* : le message du tag est proposé (`Release 1.5.0`), modifiable.

Le Finish d'une release fait, dans l'ordre :

1. merge `--no-ff` dans `master` ;
2. **tag annoté** `1.5.0` sur master (ou `v1.5.0` avec le préfixe `v`) ;
3. merge `--no-ff` dans `develop` ;
4. suppression de la branche, retour sur develop.

> 💡 Cochez « Pousser » pour envoyer master, develop et le tag en une fois.

## 7. Un hotfix

Pour corriger la production, le hotfix est la **seule** branche qui part directement de `master`.

1. *Démarrer une branche* > *Hotfix* : la version proposée est celle du dernier tag avec le *patch* + 1 (`1.5.0` → `1.5.1`).
2. Corrigez, commitez.
3. *Terminer…* : merge dans master avec le tag, puis merge dans develop.

> **Hotfix pendant une release** : si une branche `release/` est en cours, le hotfix est mergé dans master puis **dans la release** au lieu de develop. La correction rejoindra develop au Finish de la release.

Il ne peut y avoir qu'une release et qu'un hotfix en cours à la fois.

## 8. Conflits et reprise

Si un merge du Finish rencontre un conflit, l'extension **s'arrête immédiatement et ne supprime rien**. Le message liste les fichiers en conflit, et le dépôt reste dans l'état de merge.

1. Résolvez les conflits, avec l'éditeur de fusion de Visual Studio ou à la main, et enregistrez.
2. Marquez les fichiers comme résolus : *Indexer* (Stage) dans Git Changes, ou `git add <fichier>`.
3. **Ne commitez pas vous-même** : cliquez sur *Reprendre le finish…* dans la fenêtre, ou relancez *Terminer…*.

Le Finish conclut le merge, saute les étapes déjà faites (merge dans master, tag) et termine le reste.

> **Le merge a déjà été conclu par Visual Studio ?** La branche apparaît avec le badge *déjà mergée*. *Terminer…* sur sa ligne crée le tag s'il manque, puis supprime la branche.
> Pour tout annuler à la place : `git merge --abort`.

## 9. Paramètres

Ouvrez-les par le bouton *Paramètres* de la fenêtre, ou par **Extensions > GitFlow > Paramètres GitFlow…**

```text
┌─ Paramètres GitFlow ────────────────────────────────┐
│ Portée          (•) Ce dépôt   ( ) Tous mes dépôts  │
│ Branches permanentes                                │
│   Production      [master        ▾]                 │
│   Développement   [develop       ▾]                 │
│ Préfixes                                            │
│   Feature  [feature/]   Release [release/]          │
│   Hotfix   [hotfix/ ]   Tag     [        ]          │
│ Options par défaut du Finish                        │
│   Mode  (•) Merge local  ( ) Pull Request           │
│   [ ] Pousser les branches mergées (et le tag)      │
│   [ ] Supprimer aussi la branche sur origin         │
│   [ ] Pull Request : Finaliser (git fetch --prune)      │
│ [Réinitialiser aux valeurs par défaut]        [OK]  │
└─────────────────────────────────────────────────────┘
```

- **Portée** : *Ce dépôt* (git config locale) ou *Tous mes dépôts* (git config globale, qui sert de valeurs par défaut aux nouveaux dépôts).
- **Seul le nommage est configurable**, pas le comportement du flow.
- **Préfixe de tag** : vide par défaut (tags `1.4.0`), ou `v` (tags `v1.4.0`).
- **Validation en direct** : préfixe vide ou en double, production identique au développement. Les noms refusés par git sont signalés à l'enregistrement, et rien n'est écrit.
- Changer un préfixe **ne renomme pas** les branches existantes : un avertissement les liste avant d'enregistrer.

Toutes les clés sont décrites dans la [référence](reference.md#clés-de-configuration).

## 10. Les commandes du menu

Tout est aussi accessible par **Extensions > GitFlow**, sur la solution ouverte :

| Commande | Effet |
|---|---|
| Fenêtre GitFlow | Ouvre la fenêtre décrite plus haut. |
| Démarrer une feature / une release / un hotfix… | Demande le nom (version pré-remplie pour une release ou un hotfix) et crée la branche. |
| Terminer la branche actuelle… | Finish de la branche actuelle, ou reprise d'un Finish arrêté sur un conflit. |
| Pousser la branche actuelle | `git push -u origin <branche>`. |
| Paramètres GitFlow… | Écran des paramètres, ou d'initialisation si le dépôt ne l'est pas. |
| Initialiser GitFlow… | Écran d'initialisation. |

---

← [Accueil](README.md) · [Le mode Pull Request](pull-requests.md) →
