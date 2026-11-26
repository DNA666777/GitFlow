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
  - Cible : **VS 2022 17.14+ et VS 2026** (décision 2026-11-24). Packages `Microsoft.VisualStudio.Extensibility.Sdk` / `.Build` **17.14.x** :
    une version 17.N du SDK exige VS ≥ 17.N, et les 18.x sont compilées pour net10 (VS 2026 uniquement). La plage d'installation
    `[17.14,)` est générée automatiquement dans le `.vsixmanifest`. Ne pas monter en 18.x tant que VS 2022 est supporté.
  - L'API Output window est marquée *preview* en 17.14 (`VSEXTPREVIEW_OUTPUTWINDOW` supprimé dans le csproj, usage isolé dans `Services/GitFlowOutput.cs`).
  - Ne PAS utiliser le VSSDK in-process (`AsyncPackage`) sauf si une fonctionnalité indispensable est introuvable en out-of-process — dans ce cas, demander avant.
- **Langage** : C# (dernière version supportée), `nullable enable`, `TreatWarningsAsErrors` dans `GitFlow.Core`.
- **Framework** : `net8.0` pour `GitFlow.Core` et les tests, `net8.0-windows8.0` pour `GitFlow.VSExtension`.
- **UI** : Remote UI (XAML + ViewModel `NotifyPropertyChangedObject`) pour la tool window.
- **Moteur Git** : appel du **`git` CLI** (Git for Windows) via `Process`, pas LibGit2Sharp.
  Raisons : respecte la config utilisateur (credentials, hooks, signatures GPG, LFS), comportement identique au terminal.
- **Aucune dépendance** à git-flow / git-flow-avh / git-flow-next : la logique GitFlow est implémentée dans `GitFlow.Core`.
- **Tests** : xUnit + Shouldly (FluentAssertions v8 est sous licence commerciale). Pas de bibliothèque de mock : `FakeGitRunner` écrit à la main.

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
  Options par défaut du Finish (propres à l'extension, hors de l'espace `gitflow.*`) : `gitflowvs.finish.push`, `gitflowvs.finish.deleteremote`
  (booléens, local prioritaire sur global, `false` par défaut).

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

### Phase 2 — Finish par Pull Request (phase 1 validée et livrée en 1.0.0 le 2026-11-24)

Contexte : sur certains repos, `master` et `develop` sont protégées côté serveur → aucun merge direct n'est possible, tout passe par PR.
**Le mode PR est une OPTION, pas le mode par défaut** (décision utilisateur du 2026-11-24) : le Finish local reste le défaut.
- Option « Finaliser la branche » (/ « Complete branch ») avec `git fetch --prune` (nettoie les `origin/*` des branches supprimées sur le serveur) :
  clé `gitflowvs.pr.prune`, booléen, **désactivée par défaut** ; ne touche jamais aux branches locales.
- Paramètres : « Mode de Finish par défaut » = *Merge local* (défaut) ou *Pull Request*, clé `gitflowvs.finish.mode` = `local` | `pullrequest`
  (même portée locale/globale que les autres options).
- Boîte de dialogue de Finish : choix *Merge local / Pull Request* pré-sélectionné d'après ce paramètre, modifiable à chaque Finish.
  *Pull Request* est grisé (avec la raison) si le remote n'est ni GitHub ni Azure DevOps.

**Conception dès la phase 1** : le Finish doit passer par une abstraction `IFinishStrategy` (`LocalMergeStrategy` en phase 1,
`PullRequestStrategy` en phase 2) pour que la phase 2 se branche sans réécrire `FlowService`.
Plateforme Git derrière une interface `IPullRequestProvider` (plateformes cibles : **GitHub et Azure DevOps**, voir fin de section).

Flow en mode PR :
| Type    | Au Finish                                                        | Après merge de la PR vers master |
|---------|------------------------------------------------------------------|----------------------------------|
| feature | push de la branche + PR `feature/x` → develop                    | —                                |
| release | push + PR `release/x` → master **et** PR `release/x` → develop   | tag sur le commit de merge de master |
| hotfix  | push + PR `hotfix/x` → master **et** PR `hotfix/x` → develop (ou → release en cours) | tag sur le commit de merge de master |

- Le tag ne peut être posé qu'une fois la PR vers master mergée : la tool window affiche les PR ouvertes du flow,
  détecte quand celle vers master est mergée et propose « Créer le tag `{prefix}{version}` » (créé puis poussé).
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

**Plateformes cibles (décision du 2026-11-24) : GitHub et Azure DevOps (Azure Repos)** — deux implémentations de `IPullRequestProvider`.
- Détection d'après l'URL du remote `origin` : `github.com` → GitHub ; `dev.azure.com/{org}/{projet}/_git/{repo}` ou `{org}.visualstudio.com/…` → Azure DevOps.
  Remote non reconnu → mode PR indisponible, le Finish local reste utilisable.
- **Azure DevOps Server d'entreprise (décision du 2026-11-26)** : URL non reconnaissables automatiquement → l'utilisateur coche
  « origin est un serveur Azure DevOps » dans les paramètres (clé `gitflowvs.pr.platform` = `auto` | `azuredevops`, même portée que les autres options).
  Toute URL `…/_git/{repo}` est alors acceptée : API appelée sur la partie avant `_git/` (en https si le remote est en ssh), `api-version=4.1` (TFS 2018+).
  Authentification : identifiants git s'il y en a (PAT, Basic), sinon session Windows (NTLM/Kerberos, `HttpClient` dédié `UseDefaultCredentials`),
  comme git lui-même sur ces serveurs.
- API REST de chaque plateforme (GitHub REST API ; Azure DevOps `…/_apis/git/repositories/{repo}/pullrequests`).
- **Authentification : via le Git Credential Manager** (`git credential fill` / `git credential approve|reject`, appelés par `IGitRunner`),
  c.-à-d. les identifiants que git utilise déjà pour push/fetch. **Pas de `gh` ni d'`az`**, aucun token stocké par l'extension
  (l'extension reste dépendante de Git for Windows seulement). Ne jamais écrire un token dans l'Output Pane ni dans les logs.
- Côté Azure DevOps, les « branch policies » jouent le rôle des branches protégées.

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

## Phase 3 — Extension VS Code (choix validés le 2026-11-26)

Objectif : le même GitFlow dans **Visual Studio Code** (et ses forks : VSCodium, Cursor…), avec exactement le même comportement
que dans Visual Studio. Décision du 2026-11-26 : **même dépôt**, `GitFlow.Core` réutilisé tel quel (pas de réécriture en TypeScript).
Identifiant de l'extension VS Code : **`DNA666777.gitflow-vscode`** (nom affiché « GitFlow ») — `DNA666777.gitflow` est déjà pris par
l'extension Visual Studio sur le Marketplace (noms uniques par éditeur). **Ne jamais le changer** une fois publié.

### Architecture
```
src/GitFlow.Core/          # inchangé, partagé (règle d'or : toute la logique ici)
src/GitFlow.Server/        # NOUVEAU — console .NET 8 : expose le Core en JSON-RPC sur stdin/stdout
src/GitFlow.VSExtension/   # Visual Studio (inchangé)
src/vscode/                # NOUVEAU — extension VS Code en TypeScript : affichage, saisie, appels au serveur
tests/GitFlow.Server.Tests/  # NOUVEAU — chaque méthode JSON-RPC testée sur un vrai dépôt temporaire
```
- `GitFlow.Server` est une couche fine (comme `GitFlow.VSExtension`) : une méthode JSON-RPC par opération du Core
  (`getState`, `init`, `start`, `prepareFinish`, `finish`, `publish`, `commit`, `getSettings`, `saveSettings`, `pullRequests.*`).
  Les `FlowResult` sont renvoyés tels quels (statut, erreur typée, message déjà traduit) ; aucune logique métier côté TypeScript.
- Le journal git (`IGitCommandLog`) est envoyé en notification JSON-RPC et affiché dans un Output Channel « GitFlow » de VS Code.
- Langue : l'extension transmet `vscode.env.language` au démarrage du serveur → `FlowMessages.Culture` (même règle `fr*` → français).
  Textes propres à VS Code : `package.nls.json` / `package.nls.fr.json` et `l10n/bundle.l10n.fr.json`.

### Choix validés (2026-11-26)
1. **Runtime .NET** — retenu : extension **framework-dependent**, runtime .NET 8 obtenu à la demande par l'extension Microsoft
   *.NET Install Tool* (`ms-dotnettools.vscode-dotnet-runtime`, commande `dotnet.acquire`, déclarée en `extensionDependencies`),
   comme le fait l'extension C#. Paquet léger (~1 Mo) et un seul `.vsix` pour tous les OS.
   (Écarté : un `.vsix` par plateforme avec le serveur self-contained, ~30-70 Mo chacun.)
2. **Dépendances nouvelles** (approuvées) :
   - .NET : `StreamJsonRpc` (Microsoft) dans `GitFlow.Server` ;
   - npm : `vscode-jsonrpc`, `@types/vscode`, `typescript`, `esbuild` (bundle), `@vscode/vsce` (paquet), `@vscode/test-cli` + `@vscode/test-electron` (tests).
3. **Périmètre de la première version** — retenu :
   - Vue « GitFlow » dans le panneau *Source Control* (TreeView) : branche courante + type, branches en cours groupées par type,
     actions en ligne (Push, Finish), section Pull requests (états, Créer le tag, Finaliser) ;
   - Commandes dans la palette : *GitFlow: Initialize / Start Feature / Start Release / Start Hotfix / Finish Current Branch / Push Current Branch / Settings* ;
   - Finish : choix Merge local / Pull Request, options et message du tag via `QuickPick` / `InputBox` ;
     relecture des messages de PR dans un éditeur Markdown temporaire, validé par un bouton ;
   - Paramètres : `QuickPick` en plusieurs étapes, ou une webview simple (même validation que dans VS, faite par le Core) ;
   - **Hors périmètre v1** : la section Commit (VS Code a déjà la sienne ; le message généré pourrait être proposé plus tard
     via l'API `git` de VS Code).
4. **Publication** — retenu : même version que l'extension VS (un tag → les deux), **Marketplace VS Code seulement** pour l'instant,
   avec le même éditeur `DNA666777` (`vsce publish`, secret `VS_MARKETPLACE_PAT` existant si son scope le permet).
   Le `.vsix` VS Code est aussi attaché à la GitHub Release. **Open VSX** (VSCodium, Cursor…) : étape ultérieure (secret `OVSX_PAT` à créer).

### Points d'attention
- Multi-plateforme : le Core lance `git` et n'a pas de code Windows, sauf l'authentification Windows intégrée (Azure DevOps Server),
  qui dépend de la configuration Kerberos hors Windows — à documenter comme limite.
- Le serveur garde les mêmes règles de sécurité : rien n'est stocké, aucun secret dans le journal.
- Tests : `GitFlow.Server.Tests` (xUnit, protocole JSON-RPC de bout en bout) + quelques tests d'intégration de l'extension VS Code
  (activation, commandes enregistrées) lancés par `@vscode/test-cli`. Le Core reste couvert par ses tests actuels.

### Protocole de `GitFlow.Server` (étape 1, faite le 2026-11-26)
- JSON-RPC 2.0 sur stdin/stdout, en-têtes `Content-Length` (cadrage de `vscode-jsonrpc`), JSON camelCase, enums en chaînes camelCase
  (`RpcHost.JsonOptions`). stdout ne porte que le protocole ; diagnostics sur stderr ; fermer stdin arrête le serveur.
- Méthodes (paramètres nommés) : `initialize(culture)`, `findRepository(path)`, `getState(repository)`, `getSettings(repository, scope)`,
  `getLocalBranches`, `getSettingsWarnings(repository, proposed)`, `saveSettings(repository, settings, scope)`, `init(repository, settings)`,
  `suggestVersion(repository, type)`, `countUnreleasedCommits`, `start(repository, type, name)`, `prepareFinish(repository, branch?)`,
  `finish(repository, branch, tagMessage?, pushAfterFinish, deleteRemoteBranch)`, `preparePullRequests(repository, branch)`,
  `finishWithPullRequests(repository, branch, tagMessage?, drafts)`, `publish(repository, branch?)`, `getPullRequests(repository, refresh)`,
  `tagPullRequest(repository, branch)`, `completePullRequests(repository, branch)`. Notification serveur → client : `log(line)`.
- Les échecs attendus reviennent en `ResultDto` (`success`, `error`, `message` déjà traduit, `warnings`, `conflictedFiles`, `pullRequests`),
  jamais en erreur JSON-RPC. Les opérations s'exécutent une à la fois.
- Logique partagée avec VS déplacée dans le Core : `FinishPlanner` (contexte du Finish) et `PullRequestService.GetGroupsAsync`
  (actions Créer le tag / Finaliser). `FlowUi` de l'extension VS pourra s'appuyer dessus plus tard.

### Étapes (une fois validé)
(1) `GitFlow.Server` + tests (fait), (2) squelette de l'extension VS Code (activation, runtime, connexion, Output Channel) (fait),
(3) commandes Start/Finish/Push en merge local, (4) vue Source Control, (5) paramètres, (6) mode Pull Request,
(7) traduction, doc FR/EN, CI et publication sur le Marketplace VS Code, (8) plus tard : Open VSX.

---

## Conventions de code

- Code, identifiants et commentaires de code en **anglais**.
- Textes affichés et textes générés (messages de commit, descriptions de PR) : **anglais par défaut** (`Strings.resx`),
  **traduction française** (`Strings.fr.resx`), dans `GitFlow.Core/Resources` et `GitFlow.VSExtension/Resources` (décision du 2026-11-25,
  version 1.7.0). La langue suit `CurrentUICulture` (langue de VS) : `fr*` → français, toute autre langue → anglais.
  **Toute nouvelle clé va dans les deux fichiers** (test `French_translation_has_every_key_of_the_english_resources`).
  Libellés de menu : `.vsextension/string-resources.json` (anglais) et `.vsextension/fr/string-resources.json` (prise en compte par VS
  non documentée, à vérifier). Les noms de branches, titres de PR (`Feature: x`, `Release 1.7.0`) et messages de tag restent en anglais.
- Tests qui changent `FlowMessages.Culture` (statique) : dans la collection xUnit `MessagesCulture.Collection` ; les tests d'intégration
  fixent `fr-FR` (assertions en français, indépendantes de la langue de la machine).
- Libellé « Publish » de l'interface : **Pousser** / **Push** (décision du 2026-11-25) ; l'opération Publish du modèle reste `git push -u origin <branche>`.
- Vocabulaire français (décision du 2026-11-25) : garder le jargon git en anglais (commit, merge, push, fetch, stash, tag, stagé, remote, checkout),
  pas de traductions littérales (« arbre sale », « poser le tag », « clore », « distant », « récupérer ») ni de formules marketing.
- Documentation : anglais dans `docs/en/` (Markdown + site HTML) et `README.md`, français dans `docs/` et `README.fr.md`.
  Toute modification de la doc se fait dans les deux langues.
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
# Build complet (produit aussi le .vsix, MSBuild de VS non requis)
dotnet build GitFlowVS.sln -c Debug
#   → src/GitFlow.VSExtension/bin/Debug/net8.0-windows8.0/GitFlow.VSExtension.vsix

# Tests (intégration : plusieurs minutes, vrais dépôts git dans %TEMP%\gitflow-tests)
dotnet test tests/GitFlow.Core.Tests
dotnet test tests/GitFlow.Core.IntegrationTests
dotnet test tests/GitFlow.Server.Tests          # serveur JSON-RPC de l'extension VS Code (vrais dépôts + vrai processus)

# Extension VS Code (src/vscode, Node.js 20+) — npm install une fois
cd src/vscode
npm test            # publie le serveur dans server/, compile, lance les tests dans un VS Code de test (téléchargé dans .vscode-test/)
npm run package     # → src/vscode/gitflow-vscode-<version>.vsix (serveur .NET inclus, ~1,6 Mo)
#   Installation manuelle : code --install-extension src/vscode/gitflow-vscode-<version>.vsix
#   Les tests utilisent le dotnet du PATH (gitflow.dotnetPath dans les réglages de l'instance de test, cf. .vscode-test.mjs).
#   npm 11 bloque les scripts d'installation (esbuild, @vscode/vsce-sign) : inutiles ici, laissés bloqués.

# Test manuel — le F5 ne charge PAS l'extension pour l'instant (à investiguer).
# Contournement : incrémenter <Version> dans GitFlow.VSExtension.csproj, rebuild, puis installer le .vsix
# (VS 2026 passe la main au VS Installer, fenêtre interactive ; installation par machine, donc aussi dans la VS principale) :
& "H:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe" /instanceIds:f2c0b313 src\GitFlow.VSExtension\bin\Debug\net8.0-windows8.0\GitFlow.VSExtension.vsix
# puis redémarrer VS. Fermer VS AVANT d'installer : sinon le VS Installer se ferme sans rien installer.
# Après avoir changé <Version>, rebuild avec --no-incremental : sinon le .vsix garde l'ancienne version.
# Vérifier la version réellement installée : IDE\VSExtensions\*\extension.vsixmanifest (Identity Version).
# Dépôt jetable pour les essais : H:\Dev\GitFlowSandbox (origin : H:\Dev\GitFlowSandbox-origin.git).
# Logs de l'extension (exceptions) : %TEMP%\servicehub\logs\*GitFlow.VSExtension*.log
```

SDK .NET figé par `global.json` (10.0.x, `rollForward: latestFeature`) ; les projets ciblent toujours net8.0.

**Publication (GitHub Actions)** :
- `.github/workflows/ci.yml` : build Release + tests sur push `develop`/`master` et sur chaque PR.
- `.github/workflows/release.yml` : sur un tag de version (`1.2.3` ou `v1.2.3`), build Release avec `-p:Version=<tag>`
  (la `<Version>` du csproj ne sert qu'aux installations locales), tests, vérification de la version du .vsix,
  puis GitHub Release avec `GitFlow.VSExtension-<version>.vsix`. Relance manuelle possible (*Run workflow* + tag).
- Un workflow ne se déclenche que sur les commits qui le contiennent : il n'agit qu'à partir de la première release
  qui inclut `.github/workflows/`.
- Paquet (fiche de l'extension, `ExtensionEntrypoint.cs`) : éditeur `DNA666777`, `Preview = false`, licence **MIT** (`LICENSE` à la racine,
  copié en `LICENSE.txt` dans le .vsix par la cible `CopyLicenseForVsix` du csproj), icône `Resources/icon.png` (128 px) et
  `Resources/preview.png` (200 px), liens MoreInfo/ReleaseNotes vers GitHub, tags de recherche.
  **Ne jamais changer l'`id` de l'extension** : VS ne reconnaîtrait plus les mises à jour (deux extensions installées).
- **Visual Studio Marketplace** : dernière étape de `release.yml`, exécutée seulement si le secret GitHub `VS_MARKETPLACE_PAT` existe
  (jeton Azure DevOps, scope *Marketplace > Publish*, organisations : *All accessible organizations*). `VsixPublisher.exe` du paquet NuGet
  `Microsoft.VSSDK.BuildTools` téléchargé au moment du job (pas une dépendance du projet), **version figée** par `VSSDK_VERSIONS`
  dans `release.yml` (17.14.2142 d'abord). La 18.12.1583 — celle livrée avec VS 2026 et la dernière du paquet NuGet — échoue
  hors de VS (`VsixPub0031`, `Microsoft.Identity.Client` en mauvaise version, constaté le 2026-11-24) ; seul ce défaut précis
  fait essayer la version suivante, toute autre erreur arrête la publication. La 17.14 plante après l'envoi dans sa télémétrie
  (code -532462766) : le succès est donc détecté par le message `VsixPub0038` (« Uploaded »), pas par le code retour.
  Le nom affiché de l'éditeur sur le Marketplace doit être **identique** à `publisherName` de l'extension (`DNA666777`), sinon VsixPub0029.
  Fiche publiée : https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow (première version : 1.6.0, le 2026-11-25). Avec
  `marketplace/publishManifest.json` (éditeur `DNA666777`, fiche `DNA666777.gitflow`, catégorie *source control*) et
  `marketplace/overview.md` (page de présentation). Le Marketplace refuse de republier une version déjà publiée.
  Alternative future : `-oidc` (trusted publishing) au lieu d'un jeton.
Note : `dotnet sln add` du SDK 11 échoue sur cette solution (« Solution folder 'src' already contains… ») —
ajouter les projets en éditant `GitFlowVS.sln` à la main.

Mettre à jour cette section si les commandes réelles diffèrent.

---

## Façon de travailler

- Avancer par étapes livrables :
  - **Phase 1 (base)** : (1) `GitFlow.Core` + tests, (2) commandes de menu minimales, (3) tool window, (4) paramètres de nommage et options.
  - **Phase 2** : Finish par Pull Request + génération automatique du message de PR (voir section dédiée). Ne pas commencer avant validation de la phase 1 par l'utilisateur.
  - **Phase 3** : extension VS Code (voir section dédiée ; choix validés le 2026-11-26), par les étapes listées dans cette section.
- Après chaque modification : build + tests. Corriger jusqu'à ce que tout passe avant de passer à la suite.
- Le test manuel dans l'instance expérimentale de VS est fait par l'utilisateur : à la fin d'une étape touchant l'UI, indiquer précisément quoi vérifier.
- En cas de doute sur une API VisualStudio.Extensibility, consulter la doc officielle Microsoft et les samples du repo `microsoft/VSExtensibility` plutôt que deviner.
- Demander avant : ajout d'une dépendance NuGet hors liste ci-dessus, passage au VSSDK in-process, changement du modèle GitFlow. Ne jamais ajouter de type de branche ou de variante de flow non standard.
