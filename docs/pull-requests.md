# Le mode Pull Request

[Accueil](README.md) · [Guide](guide.md) · **Mode Pull Request** · [Référence et dépannage](reference.md) · [English](en/pull-requests.md)

Le mode Pull Request est une option du Finish, pour les dépôts où `master` et `develop` sont protégées. Au lieu de merger en local, l'extension pousse la branche et ouvre les pull requests. La revue et le merge restent faits par des humains.

**Sommaire**

1. [Quand l'utiliser](#quand-lutiliser)
2. [Prérequis](#prérequis)
3. [Une feature par PR](#une-feature-par-pr)
4. [Le message généré](#le-message-généré)
5. [Suivre les PR](#suivre-les-pr)
6. [Release et hotfix : le tag](#release-et-hotfix--le-tag)
7. [Finaliser la branche](#finaliser-la-branche)
8. [GitHub et Azure DevOps](#github-et-azure-devops)

---

## Quand l'utiliser

| | Merge local (défaut) | Pull Request (option) |
|---|---|---|
| Branches protégées sur le serveur | Push refusé | ✅ Prévu pour |
| Revue de code | — | ✅ Sur la plateforme |
| Rapidité | ✅ Immédiat | Après validation de la PR |
| Remote requis | Non (ou n'importe lequel) | GitHub ou Azure DevOps |

Le choix se fait **à chaque Finish**, dans la boîte *Terminer*. Pour pré-sélectionner *Pull Request* sur un dépôt, réglez « Mode de Finish par défaut » dans les [paramètres](guide.md#9-paramètres).

## Prérequis

- Le remote `origin` pointe vers **GitHub** (`github.com`) ou **Azure DevOps** (`dev.azure.com` ou `*.visualstudio.com`), en https ou en ssh.
- **Git connaît déjà vos identifiants**. C'est le cas si vous avez déjà fait un push ou un fetch en https sur ce serveur, depuis Visual Studio ou un terminal.
  - L'extension les demande à git (`git credential fill`, en pratique au Git Credential Manager).
  - Elle n'en stocke aucun, et ils n'apparaissent jamais dans les logs.
- **Aucun outil supplémentaire** : ni `gh`, ni `az`.

> ⚠️ **« Aucun identifiant git pour dev.azure.com »**
> Connectez-vous une fois : lancez `git ls-remote <url du dépôt>` dans un terminal, et la fenêtre de connexion du Git Credential Manager s'ouvre. Ensuite, réessayez.

## Une feature par PR

1. Démarrez et développez la feature comme d'habitude ([guide](guide.md#4-une-feature)).
2. *Terminer…* : choisissez **Pull Request**. Les options de push et de suppression sur origin disparaissent : elles ne servent pas en mode PR.
3. L'écran suivant montre le **titre et la description générés** pour chaque PR. Vous pouvez les modifier.
4. OK : l'extension pousse la branche puis crée la PR `feature/x` → `develop`. S'il en existe déjà une ouverte, elle est réutilisée, et vos nouveaux commits y arrivent. Rien n'est mergé en local.
5. Faites relire, puis **mergez la PR sur la plateforme**. De préférence, utilisez un merge sans fast-forward (voir [squash](#finaliser-la-branche)).
6. Dans la fenêtre GitFlow : *Actualiser les PR*, puis *Finaliser la branche*.

## Le message généré

Il est construit uniquement depuis git : il est déterministe, sans IA. Le titre suit la convention `Feature: nom` / `Release 1.5.0` / `Hotfix 1.5.1`. La description :

```markdown
**Feature** `feature/ABC-12-login` → `develop`

### Commits (2)
- Ajoute la page de connexion
- Corrige le style du bouton (#42)

### Fichiers
5 fichier(s) modifié(s), 120 ligne(s) ajoutée(s), 8 ligne(s) supprimée(s)

### Tickets
ABC-12, #42
```

Pour une release ou un hotfix, une section *Version* rappelle le tag qui sera créé après le merge.

### Personnaliser

- **Votre modèle** : `.gitflow/pull_request_template.md` dans le dépôt, avec les variables `{type}` `{source}` `{target}` `{commits}` `{stats}` `{version}` `{tickets}`.
- **Le modèle de la plateforme** (`.github/pull_request_template.md`, `.azuredevops/pull_request_template.md`…) est utilisé comme base : la description générée est ajoutée en dessous.
- **Motif des tickets** : la clé `gitflowvs.pr.ticketpattern` (expression régulière). Par défaut : `ABC-123` et `#42`.

## Suivre les PR

Une section *Pull requests* apparaît dans la fenêtre dès qu'une PR est suivie :

```text
Pull requests                          [Actualiser les PR]
release/1.5.0
   #12 → master    mergée                         [Ouvrir]
   #13 → develop   ouverte                        [Ouvrir]
                                     [Créer le tag 1.5.0]
```

- Chaque PR a sa ligne, avec son état (*ouverte*, *mergée*, *fermée sans merge*) et un bouton *Ouvrir* vers le navigateur.
- L'état est lu sur la plateforme **au plus une fois par minute** quand la fenêtre est visible, ou immédiatement avec *Actualiser les PR*.
- L'extension **n'approuve ni ne merge jamais** une PR.

## Release et hotfix : le tag

Une release ouvre **deux PR** : vers `master` et vers `develop`. Un hotfix aussi : vers master, puis vers develop (ou vers la release en cours).

Le tag ne peut être créé qu'**une fois la PR vers master mergée**, sur son commit de merge :

1. Mergez la PR vers master sur la plateforme.
2. *Actualiser les PR* : le bouton *Créer le tag 1.5.0* apparaît.
3. L'extension :
   - fait un fetch de master ;
   - vérifie que le commit de merge est bien sur `origin/master` ;
   - crée le tag annoté, avec le message saisi au Finish ;
   - pousse le tag.

*Finaliser la branche* crée aussi le tag s'il manque, si les deux PR sont déjà mergées.

## Finaliser la branche

Quand **toutes** les PR d'une branche sont mergées, *Finaliser la branche* :

1. crée le tag s'il s'agit d'une release ou d'un hotfix et qu'il manque ;
2. fait un `git fetch`, ou `git fetch --prune` si l'option est cochée dans les paramètres ;
3. met à jour vos `develop` et `master` locales, en fast-forward uniquement ;
4. supprime la branche locale avec `git branch -d`, et oublie ses PR ;
5. vous laisse sur develop, ou sur la release en cours.

> ⚠️ **Merge par « squash » ou « rebase »** : les commits de votre branche ne sont alors pas dans develop. L'extension le détecte et **ne supprime pas** la branche. Supprimez-la vous-même si vous êtes sûr. Pour éviter ça, choisissez le merge classique : *Merge*, ou *Merge (no fast-forward)*.

> 💡 **Branches sur le serveur** : Finaliser ne supprime pas la branche sur le serveur. Pour ça :
> - sur Azure DevOps, cochez *Delete branch after merging* ;
> - sur GitHub, activez *Automatically delete head branches*.
>
> Avec l'option *git fetch --prune*, les références `origin/…` correspondantes disparaissent ensuite toutes seules de votre poste.

## GitHub et Azure DevOps

| | GitHub | Azure DevOps |
|---|---|---|
| URL du remote | `https://github.com/org/repo.git`<br>`git@github.com:org/repo.git` | `https://dev.azure.com/org/projet/_git/repo`<br>`git@ssh.dev.azure.com:v3/org/projet/repo`<br>`https://org.visualstudio.com/projet/_git/repo` |
| Identifiants | Ceux de git pour `github.com` | Ceux de git pour `dev.azure.com` (compte Microsoft ou jeton personnel) |
| Branches protégées | Branch protection rules | Branch policies |
| Supprimer la branche après merge | *Delete branch* sur la PR, ou réglage du dépôt | Case *Delete … after merging* au Complete |

Avec un remote `ssh`, l'extension demande quand même des identifiants https au Git Credential Manager pour appeler l'API. Connectez-vous une fois en https si besoin.

---

← [Guide](guide.md) · [Référence et dépannage](reference.md) →
