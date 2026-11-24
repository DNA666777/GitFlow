# CLAUDE.md — Extension GitFlow pour Visual Studio 2022 / 2026

## Objectif

Extension Visual Studio permettant d'appliquer un GitFlow **standard et simple** sans quitter l'IDE :
démarrer / terminer des branches `feature`, `release`, `hotfix` en un ou deux clics,
en respectant **strictement** le Gitflow Workflow standard (Atlassian), sans ajout ni variante.

Projet réécrit **from scratch** (pas de portage de GitFlow.VS de Jakob Ehn, basé sur Team Explorer et incompatible VS 2026).

Priorités, dans l'ordre : **fiabilité** (ne jamais perdre de travail) > **simplicité d'usage** > fonctionnalités.

---

## Stack technique

- **SDK** : `Microsoft.VisualStudio.Extensibility` (modèle *out-of-process*, VisualStudio.Extensibility).
  - Cible : VS 2022 (17.9+) et VS 2026. Vérifier la plage `InstallationTargetVersion` dans la doc Microsoft à jour avant de la figer.
  - Ne PAS utiliser le VSSDK in-process (`AsyncPackage`) sauf si une fonctionnalité indispensable est introuvable en out-of-process — dans ce cas, demander avant.
- **Langage** : C# (dernière version supportée), `nullable enable`, `TreatWarningsAsErrors` dans `GitFlow.Core`.
- **Framework** : `net8.0` (ou supérieur si le SDK d'extensibilité l'exige).
- **UI** : Remote UI (XAML + ViewModel `NotifyPropertyChangedObject`) pour la tool window.
- **Moteur Git** : appel du **`git` CLI** (Git for Windows) via `Process`, pas LibGit2Sharp.
  Raisons : respecte la config utilisateur (credentials, hooks, signatures GPG, LFS), comportement identique au terminal.
- **Aucune dépendance** à git-flow / git-flow-avh / git-flow-next : la logique GitFlow est implémentée dans `GitFlow.Core`.
- **Tests** : xUnit + FluentAssertions (ou Shouldly).

---

## Architecture

```
GitFlowVS.sln
├── src/
│   ├── GitFlow.Core/            # Logique pure .NET — AUCUNE référence Visual Studio
│   │   ├── Git/                 # IGitRunner, GitCliRunner, parsing des sorties git
│   │   ├── Flow/                # FlowService : Start/Finish/Publish par type de branche
│   │   ├── Config/              # GitFlowConfig (lecture/écriture git config `gitflow.*`)
│   │   └── Model/               # BranchType, FlowBranch, RepoState, FlowResult
│   └── GitFlow.VSExtension/     # Couche VS fine : commandes, tool window, notifications
│       ├── Commands/
│       ├── ToolWindows/
│       └── Services/            # Récupération du repo de la solution ouverte, output pane
└── tests/
    ├── GitFlow.Core.Tests/              # Tests unitaires (IGitRunner mocké)
    └── GitFlow.Core.IntegrationTests/   # Tests sur de vrais dépôts git temporaires
```

**Règle d'or** : toute logique métier va dans `GitFlow.Core`. `GitFlow.VSExtension` ne fait qu'afficher, collecter
l'input utilisateur et appeler le Core. Si un bug peut être reproduit sans VS, il doit l'être par un test dans Core.

---

## Modèle GitFlow

### Branches permanentes
| Branche   | Rôle                            | Défaut    |
|-----------|---------------------------------|-----------|
| master    | Production, uniquement des tags | `master`  |
| develop   | Intégration, branche de dev principale | `develop` |

**Référence du modèle : Atlassian — Gitflow Workflow**
https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow
En cas de doute sur un comportement, c'est ce document qui fait foi. Seule différence voulue : la branche de production s'appelle `master` par défaut (Atlassian utilise `main`), ce qui est configurable.

Principes (Atlassian) :
1. `develop` est créée depuis `master` et sert de branche d'intégration ; elle porte l'historique complet, `master` un historique abrégé des versions publiées.
2. Les features partent de `develop` et y reviennent ; elles n'interagissent **jamais** directement avec `master`.
3. Quand assez de features sont prêtes, une branche `release/` part de `develop`. À partir de là : seulement corrections de bugs, documentation et tâches de release — plus de nouvelles features.
4. Une release terminée est mergée dans `master`, `master` est taggée avec le numéro de version, puis la release est mergée dans `develop` et supprimée.
5. Pour un problème en production, une branche `hotfix/` part de `master` — c'est la **seule** branche qui part directement de `master`.
6. Un hotfix terminé est mergé dans `master` (taggé avec la nouvelle version) et dans `develop` (ou dans la release en cours), puis supprimé.

On ne commite jamais directement sur `master`.

### Branches de travail
Uniquement les trois types du modèle standard — **aucun autre type** (pas de fix/, chore/, bugfix/, support/, ni de types personnalisés).

| Type      | Préfixe    | Part de   | Se termine dans          | Tag |
|-----------|------------|-----------|--------------------------|-----|
| feature   | `feature/` | develop   | develop                  | non |
| release   | `release/` | develop   | master **puis** develop  | oui |
| hotfix    | `hotfix/`  | master    | master **puis** develop  | oui |

- Sur une branche `release/*`, afficher un rappel discret « Release en cours : corrections, docs et tâches de release uniquement » (rappel, pas un blocage).
- Tags : `{prefix}{version}`, version = nom de la branche release/hotfix (`release/1.4.0` → `1.4.0`).
  Préfixe de tag **vide par défaut** (défaut git-flow, comme indiqué chez Atlassian) ; `v` proposé comme option dans les paramètres.
- Hotfix : à Start, proposer la version suivante en incrémentant le patch du dernier tag de `master` (`1.4.0` → `1.4.1`), modifiable.
- Release : à Start, proposer la version suivante en incrémentant le minor du dernier tag (`1.4.1` → `1.5.0`), modifiable.
- Message du tag : `Release {version}` / `Hotfix {version}` par défaut, modifiable dans la boîte de dialogue de Finish.
- Si une branche `release/*` est en cours au moment de terminer un hotfix, le hotfix est mergé dans master puis dans la release au lieu de develop (comportement git-flow classique).
- Seul le **nommage** est configurable (noms des branches `master`/`develop`, préfixes des 3 types, préfixe de tag) — le comportement du flow, lui, ne l'est pas.
  Stockage dans la git config locale du repo avec les clés git-flow standard : `gitflow.branch.master`, `gitflow.branch.develop`,
  `gitflow.prefix.feature`, `gitflow.prefix.release`, `gitflow.prefix.hotfix`, `gitflow.prefix.versiontag`.

### Opérations
- **Init** : ouvre l'écran de paramètres pré-rempli avec les valeurs par défaut (`master`, `develop`, préfixes ci-dessus, préfixe de tag vide), puis crée `develop` depuis `master` si absente et écrit la config `gitflow.*`. Si une config git-flow existe déjà dans le repo, la reprendre telle quelle.
- **Start** `<type> <nom>` : `git fetch` (optionnel), checkout de la base à jour, création de `<prefix><nom>`, checkout.
- **Finish** :
  - Les merges utilisent `--no-ff` (comportement par défaut de `git flow ... finish`) pour garder la trace de chaque branche dans l'historique.
  - feature : merge `--no-ff` dans develop, suppression de la branche locale (option : distante).
  - release/hotfix : merge `--no-ff` dans master, tag annoté, merge `--no-ff` de la branche dans develop, suppression de la branche.
  - Pas de squash, pas de rebase.
- **Publish** : `git push -u origin <branche>`.
- **Finish + push** : option pour pousser master, develop et les tags à la fin.

### Phase 2 — Finish par Pull Request (après la base, ne pas implémenter en phase 1)

Contexte : `master` et `develop` sont protégées côté serveur → **aucun merge direct n'est possible, tout passe par PR**.
Le Finish local de la phase 1 sert à valider la logique et aux repos non protégés ; la phase 2 fait du mode PR **le mode par défaut**.

**Conception dès la phase 1** : le Finish doit passer par une abstraction `IFinishStrategy` (`LocalMergeStrategy` en phase 1,
`PullRequestStrategy` en phase 2) pour que la phase 2 se branche sans réécrire `FlowService`.
Plateforme Git derrière une interface `IPullRequestProvider` (plateforme cible : **à préciser**, voir fin de section).

Flow en mode PR :
| Type    | Au Finish                                                        | Après merge de la PR vers master |
|---------|------------------------------------------------------------------|----------------------------------|
| feature | push de la branche + PR `feature/x` → develop                    | —                                |
| release | push + PR `release/x` → master **et** PR `release/x` → develop   | tag sur le commit de merge de master |
| hotfix  | push + PR `hotfix/x` → master **et** PR `hotfix/x` → develop (ou → release en cours) | tag sur le commit de merge de master |

- Le tag ne peut être posé qu'une fois la PR vers master mergée : la tool window affiche les PR ouvertes du flow,
  détecte quand celle vers master est mergée et propose « Poser le tag `{prefix}{version}` et pousser ».
- Suppression de la branche locale seulement quand **toutes** ses PR sont mergées.
- Ne jamais approuver ni merger une PR à la place de l'utilisateur : l'extension crée et suit, la revue reste humaine.

**Message de PR généré automatiquement** (titre + description), affiché dans une boîte de dialogue éditable avant création :
- Titre : `Feature: <nom>` / `Release <version>` / `Hotfix <version>` (nom de branche sans préfixe, `-`/`_` → espaces).
- Description, construite à partir de git uniquement (pas d'IA, résultat déterministe) :
  - Type de branche, branche source → branche cible.
  - Liste des commits : `git log <cible>..<source> --no-merges --format=...` (sujet de chaque commit, ordre chronologique).
  - Résumé des fichiers modifiés : `git diff --stat <cible>...<source>` (nombre de fichiers, +/-).
  - Pour release/hotfix : version et rappel du tag qui sera posé après merge.
  - Références de tickets détectées dans le nom de branche et les messages de commit (motif configurable, ex. `[A-Z]+-\d+`, `#\d+`).
- Modèle de description personnalisable (fichier template avec variables `{type}`, `{source}`, `{target}`, `{commits}`, `{stats}`, `{version}`, `{tickets}`) ;
  si le repo contient un template PR de la plateforme, l'utiliser comme base.
- Tests unitaires obligatoires sur la génération du message (cas : aucun commit, commits multiples, tickets, caractères spéciaux).

Plateforme cible : **à préciser par l'utilisateur** (GitHub, Azure DevOps, GitLab, Bitbucket…). Avant de commencer la phase 2, demander laquelle
et comment s'authentifier (CLI officielle déjà connectée sur le poste de préférence — `gh`, `az repos`, `glab` — plutôt que stocker un token dans l'extension).

### Règles de sécurité (non négociables)
1. **Refuser** Start/Finish si l'arbre de travail est sale (fichiers modifiés non commités) — proposer à l'utilisateur de commiter ou stasher, ne jamais stasher automatiquement en silence.
2. **Vérifier avant d'agir** : branche base existante, nom de branche valide (`git check-ref-format --branch`), branche cible inexistante pour Start.
3. **Conflit de merge** : s'arrêter immédiatement, ne rien supprimer, laisser le repo dans l'état de merge et afficher clairement quoi faire (résoudre puis relancer Finish, ou `git merge --abort`). Finish doit pouvoir **reprendre** après résolution.
4. Ne jamais faire de `push --force`, `reset --hard`, ni supprimer une branche non mergée.
5. Toute commande git exécutée est loguée dans un Output Pane « GitFlow » (commande + code retour + stderr).

---

## Interface (VSExtension)

- **Tool window « GitFlow »** :
  - Branche courante + type détecté, état du repo (propre/sale, en avance/en retard).
  - Liste des branches de travail en cours, groupées par type.
  - Boutons : Start (choix du type + nom), Finish, Publish, Init si repo non initialisé.
  - Options de Finish dans une petite boîte de dialogue (supprimer la branche distante, pousser, message du tag).
- **Paramètres GitFlow (nommage)** — écran dédié, accessible depuis la tool window et le menu :
  - Nom de la branche de production (défaut `master`) et de la branche de dev (défaut `develop`), avec liste déroulante des branches existantes.
  - Préfixe des 3 types : `feature/`, `release/`, `hotfix/`.
  - Préfixe de tag (défaut vide, option `v`).
  - Options par défaut de Finish : supprimer la branche distante, push automatique.
  - Portée : **ce repo** (git config locale, défaut) ou **tous mes repos** (git config globale, utilisée comme valeur par défaut des nouveaux repos).
  - Bouton « Réinitialiser aux valeurs par défaut ».
  - Validation avant enregistrement : préfixes valides pour git, pas deux types avec le même préfixe, `master` et `develop` différentes.
  - Renommer un préfixe ne renomme **pas** les branches existantes : afficher un avertissement si des branches avec l'ancien préfixe existent.
- **Commandes de menu** sous `Extensions > GitFlow` (et/ou menu Git) : Start Feature, Start Release, Start Hotfix, Finish current branch, Paramètres GitFlow.
- Toutes les opérations git sont **asynchrones** et n'empêchent jamais l'UI de répondre ; afficher une indication de progression.
- Messages d'erreur en français, courts, avec l'action suggérée.

---

## Conventions de code

- Code, identifiants et commentaires de code en **anglais** ; textes affichés à l'utilisateur en **français** (centralisés dans un fichier de ressources pour permettre une traduction future).
- `async`/`await` partout pour l'I/O, `CancellationToken` propagé.
- `IGitRunner` est la seule porte vers git : `Task<GitResult> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct)`.
  Toujours passer les arguments en liste (pas de concaténation de chaînes → pas d'injection via un nom de branche).
- Forcer une sortie git stable : `-c core.quotepath=false`, `LC_ALL=C` / `LANG=C` dans l'environnement du process, `--porcelain` quand disponible.
- `FlowService` retourne des `FlowResult` (succès / échec avec raison typée / conflit), il ne lève pas d'exception pour un cas métier attendu.
- Petites classes, pas de logique dans les ViewModels au-delà de l'orchestration.

---

## Tests

- **Unitaires** (`GitFlow.Core.Tests`) : `IGitRunner` mocké, vérifier la séquence exacte de commandes git pour chaque opération et chaque cas d'erreur.
- **Intégration** (`GitFlow.Core.IntegrationTests`) : créer un dépôt temporaire (`git init` dans `%TEMP%`, + un repo bare comme `origin`), exécuter les vrais flux et vérifier
  l'état final (branches, tags, historique de merge, absence de perte de commits). Nettoyer les dossiers temporaires.
- Cas obligatoires : chaque Start/Finish par type, arbre sale, nom invalide, branche déjà existante, conflit au merge puis reprise, hotfix avec release en cours, repo non initialisé, repo sans remote,
  nommage personnalisé (ex. `main`/`dev`, préfixe `feat/`, tag sans préfixe), config git-flow existante reprise à l'Init.
- Une fonctionnalité n'est **pas terminée** tant que ses tests d'intégration ne passent pas.

---

## Commandes

```powershell
# Build complet
dotnet build GitFlowVS.sln -c Debug

# Tests
dotnet test tests/GitFlow.Core.Tests
dotnet test tests/GitFlow.Core.IntegrationTests

# Si le projet d'extension exige MSBuild de Visual Studio :
# & "$(& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -find MSBuild\**\Bin\MSBuild.exe)" GitFlowVS.sln /p:Configuration=Debug

# Lancer l'instance expérimentale pour test manuel (après build) :
# devenv /rootsuffix Exp
```

Mettre à jour cette section si les commandes réelles diffèrent.

---

## Façon de travailler

- Avancer par étapes livrables :
  - **Phase 1 (base)** : (1) `GitFlow.Core` + tests, (2) commandes de menu minimales, (3) tool window, (4) paramètres de nommage et options.
  - **Phase 2** : Finish par Pull Request + génération automatique du message de PR (voir section dédiée). Ne pas commencer avant validation de la phase 1 par l'utilisateur.
- Après chaque modification : build + tests. Corriger jusqu'à ce que tout passe avant de passer à la suite.
- Le test manuel dans l'instance expérimentale de VS est fait par l'utilisateur : à la fin d'une étape touchant l'UI, indiquer précisément quoi vérifier.
- En cas de doute sur une API VisualStudio.Extensibility, consulter la doc officielle Microsoft et les samples du repo `microsoft/VSExtensibility` plutôt que deviner.
- Demander avant : ajout d'une dépendance NuGet hors liste ci-dessus, passage au VSSDK in-process, changement du modèle GitFlow. Ne jamais ajouter de type de branche ou de variante de flow non standard.
