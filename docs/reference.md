# Référence et dépannage

[Accueil](README.md) · [Guide](guide.md) · [Mode Pull Request](pull-requests.md) · **Référence et dépannage** · [English](en/reference.md)

Les règles appliquées, les clés de configuration et les réponses aux problèmes courants.

**Sommaire**

1. [Le modèle en bref](#le-modèle-en-bref)
2. [Règles de sécurité](#règles-de-sécurité)
3. [Clés de configuration](#clés-de-configuration)
4. [Commandes git exécutées](#commandes-git-exécutées)
5. [Dépannage](#dépannage)
6. [Compatibilité](#compatibilité)

---

## Le modèle en bref

L'extension applique strictement le [Gitflow Workflow d'Atlassian](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow), sans variante. Seule différence : la branche de production s'appelle `master` par défaut. Ce nom est configurable.

| Type | Préfixe | Part de | Se termine dans | Tag | Version proposée |
|---|---|---|---|---|---|
| Feature | `feature/` | develop | develop | non | — |
| Release | `release/` | develop | master **puis** develop | oui | minor + 1 (`1.4.1` → `1.5.0`) |
| Hotfix | `hotfix/` | master | master **puis** develop (ou la release en cours) | oui | patch + 1 (`1.5.0` → `1.5.1`) |

- Il n'existe aucun autre type de branche (pas de `bugfix/`, `support/`…).
- Les merges se font en `--no-ff`, sans squash ni rebase.
- Les tags sont annotés, au format `{préfixe}{version}`. Leur message par défaut est `Release x.y.z` / `Hotfix x.y.z`, et il est modifiable.
- Une seule release et un seul hotfix peuvent être en cours à la fois.

## Règles de sécurité

| | |
|---|---|
| Modifications non commitées | Start et Finish sont refusés. L'extension ne fait jamais de stash automatique : faites un commit ou un `git stash` vous-même. Les fichiers non suivis ne bloquent pas. |
| Vérifications avant d'agir | Branche de base existante, nom valide pour git (`git check-ref-format`), branche cible inexistante, tag inexistant. |
| Conflit de merge | Arrêt immédiat, rien n'est supprimé, reprise possible après résolution. |
| Commandes interdites | Jamais de `push --force`, `reset --hard` ni `branch -D`. Une branche non mergée n'est jamais supprimée. |
| Commits | Refusés sur la branche de production et pendant un merge. |
| Pull Requests | Créées et suivies, jamais approuvées ni mergées par l'extension. Le tag n'est créé que sur un commit de merge vérifié sur `origin/master`. |
| Identifiants | Demandés à git à chaque fois, jamais stockés ni écrits dans les logs. |

## Clés de configuration

Elles sont stockées dans la git config du dépôt (`--local`) ou de l'utilisateur (`--global`). Les valeurs globales servent de valeurs par défaut aux nouveaux dépôts. La valeur locale l'emporte.

### Nommage (clés git-flow standard)

| Clé | Défaut | Rôle |
|---|---|---|
| `gitflow.branch.master` | `master` | Branche de production |
| `gitflow.branch.develop` | `develop` | Branche d'intégration |
| `gitflow.prefix.feature` | `feature/` | Préfixe des features |
| `gitflow.prefix.release` | `release/` | Préfixe des releases |
| `gitflow.prefix.hotfix` | `hotfix/` | Préfixe des hotfixes |
| `gitflow.prefix.versiontag` | *(vide)* | Préfixe des tags de version (`v` → `v1.4.0`) |

### Options de l'extension

| Clé | Défaut | Rôle |
|---|---|---|
| `gitflowvs.finish.mode` | `local` | Mode pré-sélectionné au Finish : `local` ou `pullrequest` |
| `gitflowvs.finish.push` | `false` | Pré-coche « Pousser » au Finish local |
| `gitflowvs.finish.deleteremote` | `false` | Pré-coche « Supprimer aussi la branche sur origin » |
| `gitflowvs.pr.prune` | `false` | *Finaliser la branche* utilise `git fetch --prune` |
| `gitflowvs.pr.ticketpattern` | ABC-123 et #42 | Expression régulière des références de tickets dans les PR |
| `gitflowvs.pr.platform` | `auto` | `azuredevops` : origin est un serveur Azure DevOps d'entreprise (URL non reconnue automatiquement) |

Les PR suivies sont mémorisées dans la config locale, sous `gitflowvs-pr.<branche>.link`. Elles sont effacées quand la branche est finalisée.

Exemple pour passer un dépôt en préfixe de tag `v` :

```powershell
git config --local gitflow.prefix.versiontag v
```

## Commandes git exécutées

Toutes les commandes passent par le `git` de Git for Windows : vos hooks, signatures GPG, LFS et identifiants s'appliquent comme dans un terminal. Elles sont visibles dans **Affichage > Sortie**, canal **GitFlow** :

```text
=== GitFlow : finish de feature/login ===
[10:42:03] git switch develop  → 0 (81 ms)
[10:42:03] git merge --no-ff -m "Merge branch 'feature/login' into develop" feature/login  → 0 (153 ms)
[10:42:03] git branch -d feature/login  → 0 (54 ms)
OK
```

## Dépannage

### Le menu GitFlow n'apparaît pas

Vérifiez **Extensions > Gérer les extensions > Installées**. Si l'extension n'y est pas, réinstallez le `.vsix` **avec Visual Studio fermé**. S'il est ouvert, le Visual Studio Installer se ferme sans rien installer.

### Un clic ne fait rien

Regardez le canal **GitFlow** de la fenêtre Sortie, puis le journal de l'extension : `%TEMP%\servicehub\logs\*GitFlow.VSExtension*.log`.

### « Aucun dépôt Git trouvé »

L'extension cherche le dépôt dans cet ordre : la solution ouverte, puis l'élément sélectionné, puis le document actif. Ouvrez une solution située dans un dépôt Git.

### « Des modifications ne sont pas commitées »

Faites un commit (section Commit) ou un `git stash`, puis réessayez. Les fichiers non suivis ne comptent pas.

### Mes fichiers modifiés n'apparaissent pas dans Commit

Enregistrez-les (<kbd>Ctrl</kbd>+<kbd>S</kbd>) : git ne voit que les fichiers écrits sur le disque.

### La version de release proposée a déjà un tag

La version suivante est calculée depuis le plus grand tag de version présent sur master. Tapez une autre version : une version dont le tag existe déjà est refusée.

### « Mode Pull Request indisponible »

Le remote `origin` n'est ni GitHub ni Azure DevOps (un dossier local, GitLab, Bitbucket…) : utilisez le merge local. Vérifiez l'URL avec `git remote -v`. Pour un serveur Azure DevOps d'entreprise, cochez l'option correspondante dans les paramètres ([détails](pull-requests.md#serveur-azure-devops-dentreprise)).

### « La plateforme a refusé la demande »

Le message de GitHub ou d'Azure DevOps est affiché tel quel : droits insuffisants, PR déjà existante, branche cible absente… Après correction, relancez *Terminer…* : une PR déjà ouverte est réutilisée.

### Finaliser refuse : branche non contenue dans develop

La PR a été mergée par *squash* ou *rebase* : les commits de la branche ne sont pas dans develop, donc rien n'est supprimé. Si le contenu est bien livré, supprimez la branche vous-même : `git branch -D feature/x`.

## Compatibilité

| | |
|---|---|
| Visual Studio | 2022 version 17.14 ou plus, 2026 (18.x). Éditions Community, Professional, Enterprise. |
| Git | Git for Windows 2.28 ou plus (installé avec Visual Studio) |
| Plateformes (mode PR) | GitHub (github.com), Azure DevOps (dev.azure.com, *.visualstudio.com), Azure DevOps Server / TFS 2018 ou plus (à déclarer dans les paramètres) |
| Langues | Anglais (par défaut) et français, selon la langue de Visual Studio. Les messages générés (commit, PR) suivent la même langue. |
| Configuration | Compatible avec les clés `gitflow.*` de git-flow / git-flow-avh |

---

← [Le mode Pull Request](pull-requests.md) · [Accueil](README.md)
