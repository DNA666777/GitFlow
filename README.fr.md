# GitFlow pour Visual Studio et VS Code

🌐 [English](README.md) · **Français**

Extension pour **Visual Studio** et **Visual Studio Code**, pour appliquer le **Gitflow standard** sans quitter l'éditeur. Elle démarre et termine les branches `feature/`, `release/` et `hotfix/`, en respectant strictement le [Gitflow Workflow d'Atlassian](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow).

> 🌐 **Site du tutoriel** : https://dna666777.github.io/GitFlow/
>
> 📖 **Tutoriel en Markdown** : [Accueil](docs/README.md) · [Guide](docs/guide.md) · [Mode Pull Request](docs/pull-requests.md) · [Référence et dépannage](docs/reference.md) · [VS Code](docs/vscode.md)
>
> ⬇️ **Télécharger** : Visual Studio — [Marketplace](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow) · VS Code — [Marketplace](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow-for-vscode) · les deux — [dernière release](https://github.com/DNA666777/GitFlow/releases/latest) (fichiers `.vsix`)

## Fonctionnalités

- **Le flow** :
  - **Init** : crée `develop` et écrit la config git-flow standard (`gitflow.*`).
  - **Start** : la version suivante est proposée pour une release ou un hotfix.
  - **Finish** : merges `--no-ff`, tag annoté pour une release ou un hotfix. Un hotfix lancé pendant une release est mergé dans cette release.
  - **Push** : pousse la branche sur `origin`.
- **Fenêtre GitFlow**, en onglet à côté de *Git Changes* :
  - état du dépôt et branches en cours, avec leurs boutons Pousser et Terminer ;
  - rappel « corrections, docs et tâches de release uniquement » sur une branche de release.
- **Commit** : liste des modifications et message de commit **généré depuis git** (fichiers, lignes ajoutées/supprimées, tickets du nom de branche), modifiable.
- **Mode Pull Request (option)**, pour les dépôts où `master` et `develop` sont protégées, sur **GitHub** et **Azure DevOps** :
  - message de PR généré ;
  - suivi des PR dans la fenêtre ;
  - tag créé sur le commit de merge ;
  - finalisation de la branche.
- **Paramètres** : nommage (branches, préfixes, préfixe de tag), options par défaut du Finish, portée *ce dépôt* ou *tous mes dépôts*.
- **Sécurité** :
  - Start et Finish refusés s'il y a des modifications non commitées (pas de stash automatique) ;
  - arrêt sur conflit, avec reprise du Finish ;
  - jamais de `push --force`, de `reset --hard`, ni de suppression d'une branche non mergée ;
  - chaque commande git est journalisée dans *Sortie > GitFlow*.
- **Anglais et français** : l'extension suit la langue de Visual Studio (français pour un VS en français, anglais sinon). Les messages de commit et de PR générés suivent la même langue.
- **Dépendances** : seulement Visual Studio et Git for Windows. Pas de git-flow, ni `gh`, ni `az` : le mode PR réutilise les identifiants que git connaît déjà (Git Credential Manager), sans en stocker aucun.

## VS Code

La même extension existe pour **Visual Studio Code** (Windows, Linux, macOS), avec le même moteur : un panneau GitFlow dans la vue Source Control (mêmes sections que la fenêtre Visual Studio, Commit compris), les commandes dans la palette, l'écran de paramètres et le mode Pull Request. Le runtime .NET 8 est fourni automatiquement par l'extension *.NET Install Tool*. Voir [GitFlow pour VS Code](docs/vscode.md).

## Prérequis

| | |
|---|---|
| Visual Studio | 2022 **17.14+** ou 2026 |
| Git | Git for Windows (installé avec Visual Studio), `git` dans le PATH |
| Mode Pull Request | remote `origin` sur GitHub ou Azure DevOps, identifiants git déjà configurés |

## Installation

1. **Fermez Visual Studio.**
2. Installez `GitFlow.VSExtension.vsix` : double-clic, ou en ligne de commande :
   ```powershell
   & "<dossier VS>\Common7\IDE\VSIXInstaller.exe" GitFlow.VSExtension.vsix
   ```
3. Relancez Visual Studio : le menu **Extensions > GitFlow** apparaît.

Pour obtenir le `.vsix`, voir [Compiler](#compiler) ci-dessous.

## Démarrage rapide

1. Ouvrez une solution placée dans un dépôt Git, puis **Extensions > GitFlow > Fenêtre GitFlow**.
2. **Initialiser GitFlow…** et validez les noms proposés (`master`, `develop`, `feature/`, `release/`, `hotfix/`).
3. **Démarrer une branche** : *Feature*, nom `login`, puis *Démarrer*.
4. Travaillez, puis **Commiter** depuis la section Commit.
5. **Terminer…** sur la ligne de la branche, en *Merge local* ou en *Pull Request*.

Le reste est dans le [guide](docs/guide.md).

## Le modèle appliqué

| Type | Préfixe | Part de | Se termine dans | Tag |
|---|---|---|---|---|
| feature | `feature/` | develop | develop | non |
| release | `release/` | develop | master **puis** develop | oui |
| hotfix | `hotfix/` | master | master **puis** develop (ou la release en cours) | oui |

Seul le nommage est configurable. Le comportement du flow ne l'est pas, et il n'existe aucun autre type de branche.

## Compiler

```powershell
# Build complet : produit aussi le .vsix (MSBuild de Visual Studio non requis)
dotnet build GitFlowVS.sln -c Debug
#   → src/GitFlow.VSExtension/bin/Debug/net8.0-windows8.0/GitFlow.VSExtension.vsix

# Tests
dotnet test tests/GitFlow.Core.Tests               # unitaires (git simulé)
dotnet test tests/GitFlow.Core.IntegrationTests    # vrais dépôts git dans %TEMP% (quelques minutes)
```

Après un changement de `<Version>` dans `GitFlow.VSExtension.csproj`, rebuild avec `--no-incremental` : sinon le `.vsix` garde l'ancienne version.

### Architecture

```
GitFlowVS.sln
├── src/
│   ├── GitFlow.Core/            # Toute la logique, en .NET pur, sans référence à Visual Studio
│   │   ├── Git/                 # IGitRunner (seule porte vers git), GitCliRunner, GitClient
│   │   ├── Flow/                # FlowService, LocalMergeStrategy, CommitService, générateur de message
│   │   ├── Config/              # Config gitflow.* et options gitflowvs.*
│   │   ├── PullRequests/        # Mode PR : GitHub, Azure DevOps, identifiants via git
│   │   └── Model/               # FlowResult, RepoState, BranchType…
│   └── GitFlow.VSExtension/     # Couche VS fine (VisualStudio.Extensibility, out-of-process, Remote UI)
├── tests/
│   ├── GitFlow.Core.Tests/              # Tests unitaires
│   └── GitFlow.Core.IntegrationTests/   # Tests sur de vrais dépôts git temporaires
└── docs/                        # Tutoriel (Markdown + petit site statique), en/ pour l'anglais
```

- **Stack** :
  - .NET 8, C# ;
  - SDK `Microsoft.VisualStudio.Extensibility` 17.14 (extension out-of-process) ;
  - `git` en ligne de commande, pour respecter la config de l'utilisateur : identifiants, hooks, signatures GPG, LFS ;
  - xUnit + Shouldly pour les tests.
- **Règle d'or** : toute la logique est dans `GitFlow.Core`. L'extension VS ne fait qu'afficher, recueillir les choix de l'utilisateur et appeler le Core. Un bug reproductible sans VS doit l'être par un test du Core.

## Publier une version

La publication est automatique (GitHub Actions) et suit le flow :

1. **Démarrer une release** avec l'extension (version proposée, par exemple `1.4.0`).
2. **Terminer** la release avec « Pousser » coché, ou en mode Pull Request puis *Créer le tag*. Le tag `1.4.0` arrive sur GitHub.
3. Le workflow [`release.yml`](.github/workflows/release.yml) se déclenche sur ce tag :
   - build Release, **à la version du tag** ;
   - tests unitaires et d'intégration ;
   - vérification de la version du paquet ;
   - création de la **GitHub Release** avec `GitFlow.VSExtension-1.4.0.vsix` attaché et des notes générées à partir des commits.

Pour (re)publier un tag existant : onglet *Actions* > *Release* > *Run workflow*, puis indiquer le tag.

**Visual Studio Marketplace** : si le secret `VS_MARKETPLACE_PAT` est configuré dans le dépôt, la release est aussi publiée sur le Marketplace (fiche décrite par [`marketplace/publishManifest.json`](marketplace/publishManifest.json) et [`marketplace/overview.md`](marketplace/overview.md)). Sans ce secret, l'étape est simplement sautée.

Le workflow [`ci.yml`](.github/workflows/ci.yml) compile et teste chaque push sur `develop` ou `master` et chaque pull request. Le `.vsix` de chaque build reste disponible 7 jours en artefact.

## Licence

[MIT](LICENSE) — © 2026 DNA666777.

## Documentation

Le tutoriel est dans [`docs/`](docs/README.md) en français et dans [`docs/en/`](docs/en/README.md) en anglais, chacun en deux formats au contenu identique :

- **Markdown**, lisible directement sur GitHub : [accueil](docs/README.md), [guide](docs/guide.md), [mode Pull Request](docs/pull-requests.md), [référence](docs/reference.md).
- **Petit site web statique**, sans build ni dépendance, publié sur **https://dna666777.github.io/GitFlow/** par GitHub Pages
  (branche `master`, dossier `/docs` : le site est mis à jour à chaque release). En local, ouvrez `docs/index.html` dans un navigateur.
