# GitFlow pour Visual Studio

**Le Gitflow standard, sans quitter Visual Studio.** Démarrez et terminez vos branches `feature/`, `release/` et `hotfix/` en un ou deux clics, en respectant strictement le [Gitflow Workflow d'Atlassian](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow).

*The standard Gitflow, without leaving Visual Studio: start and finish feature, release and hotfix branches in one or two clicks. Local merge by default, or Pull Requests on GitHub and Azure DevOps. **Available in English and French** (follows the Visual Studio language).*

📖 **Tutoriel complet** : https://dna666777.github.io/GitFlow/

## Fonctionnalités

- **Le flow complet**
  - **Init** : crée `develop` et écrit la config git-flow standard.
  - **Start** : version suivante proposée pour une release ou un hotfix.
  - **Finish** : merges `--no-ff`, tag annoté. Un hotfix pendant une release est mergé dans la release.
  - **Push** : pousse la branche sur origin.
- **Fenêtre GitFlow** ancrée à côté de *Git Changes* : état du dépôt, branches en cours avec leurs boutons *Pousser* et *Terminer*, rappel des règles sur une branche de release.
- **Commit intégré** : liste des modifications et message de commit **généré depuis git** (fichiers, lignes ajoutées/supprimées, tickets du nom de branche), modifiable.
- **Mode Pull Request (option)** pour les dépôts où `master` et `develop` sont protégées, sur **GitHub** et **Azure DevOps** : message de PR généré, suivi des PR dans la fenêtre, tag posé sur le commit de merge, clôture de la branche.
- **Paramètres** : noms des branches, préfixes, préfixe de tag, options par défaut du Finish, pour un dépôt ou pour tous vos dépôts.

## Ne perd jamais de travail

- Start et Finish refusés si des modifications ne sont pas commitées. Jamais de *stash* automatique.
- Sur un conflit de merge : arrêt immédiat, rien n'est supprimé, et le Finish reprend après résolution.
- Jamais de `push --force`, de `reset --hard`, ni de suppression d'une branche non mergée.
- Chaque commande git exécutée est visible dans *Sortie > GitFlow*.

## Autonome

Il suffit de Visual Studio et de Git for Windows. L'extension n'a besoin ni de git-flow, ni de `gh`, ni d'`az`. Le mode Pull Request réutilise les identifiants que git connaît déjà, via le Git Credential Manager, et l'extension n'en stocke aucun.

## Prérequis

- Visual Studio 2022 **17.14** ou plus, ou Visual Studio 2026.
- Git for Windows, installé avec Visual Studio.

## Démarrer

1. **Extensions > GitFlow > Fenêtre GitFlow**.
2. **Initialiser GitFlow…**, puis validez les noms proposés.
3. **Démarrer une branche** > *Feature*, puis travaillez et commitez.
4. **Terminer…**, en *Merge local* ou en *Pull Request*.

Le reste est dans le [guide](https://dna666777.github.io/GitFlow/guide.html). Pour signaler un problème ou suivre les versions : [GitHub](https://github.com/DNA666777/GitFlow).

Licence [MIT](https://github.com/DNA666777/GitFlow/blob/master/LICENSE).
