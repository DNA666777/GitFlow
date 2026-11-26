# GitFlow pour VS Code

[Accueil](README.md) · [Guide](guide.md) · [Mode Pull Request](pull-requests.md) · [Référence et dépannage](reference.md) · **VS Code** · [English](en/vscode.md)

La même extension GitFlow, dans **Visual Studio Code** (Windows, Linux, macOS). Le moteur est le même que dans Visual Studio : mêmes règles, mêmes commandes git, mêmes messages. Seule l'interface s'adapte à VS Code.

**Sommaire**

1. [Installation](#1-installation)
2. [Le panneau GitFlow](#2-le-panneau-gitflow)
3. [Les commandes](#3-les-commandes)
4. [Paramètres et initialisation](#4-paramètres-et-initialisation)
5. [Terminer une branche](#5-terminer-une-branche)
6. [Le journal](#6-le-journal)
7. [Dépannage](#7-dépannage)

---

## 1. Installation

- **Depuis VS Code** : vue *Extensions* (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd>), cherchez **GitFlow** (éditeur DNA666777), *Install*.
- **Depuis un fichier** : téléchargez `gitflow-vscode-<version>.vsix` sur la [page des releases](https://github.com/DNA666777/GitFlow/releases/latest), puis :

  ```powershell
  code --install-extension gitflow-vscode-<version>.vsix
  ```

Prérequis :

- VS Code 1.90 ou plus ;
- `git` dans le PATH ;
- le **runtime .NET 8**, fourni automatiquement : l'extension Microsoft *.NET Install Tool* s'installe avec GitFlow et télécharge .NET 8 une seule fois (quelques dizaines de Mo) dans le dossier de VS Code. Rien à installer à la main.

## 2. Le panneau GitFlow

Ouvrez **Source Control** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd>) : la section **GitFlow** reprend la fenêtre GitFlow de Visual Studio, dans le même ordre.

```text
GitFlowSandbox                       [Log] [Settings]
Branch   feature/login
Type     Feature
State    Uncommitted changes
Remote   1 commit(s) ahead, 0 behind

Commit
Changes (2)
 M  src/App/Program.cs
 A  src/App/Login.cs
 ┌───────────────────────────────────────────────┐
 │ Update 2 files in src/App                     │
 └───────────────────────────────────────────────┘
              [Regenerate message] [Commit]

Start a branch
[Feature ▾] feature/ [____________] [Start]

Branches in progress
Features
  login  (current)                 [Push] [Finish…]
```

- **État du dépôt** : branche actuelle et son type, modifications, avance ou retard sur origin.
- **Commit** : les fichiers modifiés et un message généré depuis git (comme dans Visual Studio). Le message reste à vous dès que vous le modifiez ; *Regenerate message* remet la proposition ; <kbd>Ctrl</kbd>+<kbd>Entrée</kbd> commite.
- **Démarrer une branche** : type, préfixe, nom ; la version suivante est pré-remplie pour une release ou un hotfix.
- **Branches en cours** avec *Push* et *Finish…* sur chaque ligne. Badges : *current*, *already merged*, *no commit yet*.
- **Pull requests**, quand des PR sont suivies : état, *Open*, *Create tag*, *Complete branch*.

Le panneau se met à jour tout seul (commit, changement de branche, enregistrement d'un fichier…), seulement quand il est visible. Le bouton ↻ du titre force l'actualisation.

## 3. Les commandes

Palette de commandes (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>), catégorie **GitFlow** :

| Commande | Effet |
|---|---|
| GitFlow: Initialize GitFlow... | Écran d'initialisation (voir plus bas). |
| GitFlow: Start Feature... / Start Release... / Start Hotfix... | Demande le nom (version pré-remplie pour release/hotfix) et crée la branche. |
| GitFlow: Finish Current Branch... | Termine la branche actuelle, ou reprend un Finish arrêté sur un conflit. |
| GitFlow: Push Current Branch | `git push -u origin <branche>`. |
| GitFlow: GitFlow Settings... | Écran des paramètres. |
| GitFlow: Show Log / Restart Server | Journal ; redémarrage du moteur. |

Les libellés suivent la langue de VS Code (anglais ou français).

## 4. Paramètres et initialisation

*Settings* dans le panneau (ou la roue dentée du titre) ouvre l'onglet **GitFlow Settings**, avec les mêmes champs que dans Visual Studio :

- portée : *ce dépôt* ou *tous mes dépôts* ;
- branches de production et de développement, préfixes, préfixe de tag ;
- options par défaut du Finish : mode Merge local ou Pull Request, push, suppression sur origin, `fetch --prune`, serveur Azure DevOps d'entreprise.

Les erreurs (préfixe vide ou en double, production = développement, nom refusé par git) s'affichent pendant la saisie et bloquent l'enregistrement.

Sur un dépôt pas encore initialisé, le même écran s'appelle **Initialize GitFlow** : vérifiez les noms, puis *Initialize* crée `develop` si besoin et écrit la configuration.

## 5. Terminer une branche

*Finish…* enchaîne :

1. une confirmation qui résume ce qui va se passer (refus immédiat s'il y a des modifications non commitées) ;
2. le **mode** : *Local merge* ou *Pull Request* (si `origin` est sur GitHub ou Azure DevOps) ;
3. en merge local, les options : push vers origin, suppression de la branche sur origin ;
4. pour une release ou un hotfix, le message du tag.

En **Pull Request**, un onglet affiche le titre et la description générés pour chaque PR, modifiables, puis *Create the pull requests* pousse la branche et crée les PR. Le suivi se fait dans la section *Pull requests* du panneau, exactement comme dans Visual Studio : voir [le mode Pull Request](pull-requests.md).

## 6. Le journal

**GitFlow: Show Log** (ou *Log* dans le panneau) ouvre le canal **GitFlow** de la vue *Output* : chaque opération et ses commandes git, avec le code retour.

Les lectures faites pour actualiser le panneau sont aussi journalisées, au niveau *Trace*, masqué par défaut. Pour les voir : *Output: Set Log Level...* > *Trace*, sur le canal GitFlow.

## 7. Dépannage

**« The .NET 8 runtime could not be obtained »** : regardez la sortie *.NET Install Tool* (proxy, réseau…). Vous pouvez aussi indiquer un `dotnet` existant dans le paramètre utilisateur `gitflow.dotnetPath` (Fichier > Préférences > Paramètres, puis cherchez « gitflow »).

**Le panneau affiche « GitFlow could not start »** : *Log* pour le détail, puis **GitFlow: Restart Server**.

**Azure DevOps Server d'entreprise sous Linux ou macOS** : sans identifiant git, l'authentification Windows intégrée dépend de la configuration Kerberos de la machine. Utilisez plutôt un jeton personnel enregistré par le Git Credential Manager.

---

← [Référence et dépannage](reference.md) · [Accueil](README.md)
