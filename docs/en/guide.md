# User guide

[Home](README.md) · **Guide** · [Pull Request mode](pull-requests.md) · [Reference and troubleshooting](reference.md) · [Français](../guide.md)

Installation, initialization, then day-to-day work: features, releases and hotfixes.

**Contents**

1. [Installation](#1-installation)
2. [The GitFlow window](#2-the-gitflow-window)
3. [Initializing a repository](#3-initializing-a-repository)
4. [A feature](#4-a-feature)
5. [Committing](#5-committing)
6. [A release](#6-a-release)
7. [A hotfix](#7-a-hotfix)
8. [Conflicts and resuming](#8-conflicts-and-resuming)
9. [Settings](#9-settings)
10. [Menu commands](#10-menu-commands)

---

## 1. Installation

1. **Close Visual Studio completely**, all windows. Otherwise the installer closes without installing anything.
2. Double-click `GitFlow.VSExtension.vsix`. Depending on the Visual Studio version, the *VSIX Installer* and then the *Visual Studio Installer* take over: confirm until the end.
3. Restart Visual Studio. The **Extensions > GitFlow** menu appears, and the extension is listed in **Extensions > Manage Extensions > Installed**.

From the command line:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe" GitFlow.VSExtension.vsix
```

> **Updating**: same procedure with the new `.vsix`. To uninstall: **Extensions > Manage Extensions**, then *Uninstall*.

## 2. The GitFlow window

Open it from **Extensions > GitFlow > GitFlow Window**, or from **View > Other Windows**. It opens as a tab next to *Git Changes*. It refreshes on its own: when a file is saved, when the branch changes, when another solution is opened.

```text
┌─ GitFlow ─────────────────────────────────────────┐
│ MyProject                       [Settings][Refresh]│
│ Branch    feature/login                            │
│ Type      Feature                                  │
│ State     Uncommitted changes                      │
│ Remote    1 commit(s) ahead, 0 behind              │
│                                                    │
│ Commit                                             │
│ Changes (2)                                        │
│  M  src/App/Program.cs                             │
│  A  src/App/Login.cs                               │
│ ┌────────────────────────────────────────────────┐ │
│ │ Update 2 files in src/App                      │ │
│ │ Added (1): - src/App/Login.cs …                │ │
│ └────────────────────────────────────────────────┘ │
│                    [Regenerate message] [Commit]   │
│                                                    │
│ Start a branch                                     │
│ [Feature ▾] feature/ [____________] [Start]        │
│                                                    │
│ Branches in progress                               │
│ Features                                           │
│   login (current)                [Push][Finish…]   │
│ Releases                                           │
│ Hotfixes                                           │
└────────────────────────────────────────────────────┘
```

What the window shows:

- **Repository state**: current branch and its type, uncommitted changes or merge in progress, commits ahead of or behind origin.
- **Commit**: the changed files and a suggested message ([details](#5-committing)).
- **Start a branch**: type, prefix and name. For a release or a hotfix, the next version is pre-filled.
- **Branches in progress**, grouped by type, with *Push* and *Finish…* on each row. You don't need to be on a branch to finish it.
- **Badges**:
  - *current*: the branch you are on;
  - *already merged*: a branch already merged by hand into all its targets.
- On a `release/` branch, the reminder "bug fixes, docs and release tasks only".
- The *Pull requests* section appears if you use [Pull Request mode](pull-requests.md).

## 3. Initializing a repository

On a repository that has never used GitFlow, the window offers *Initialize GitFlow…*. The settings screen opens, pre-filled:

- with the existing git-flow configuration, if the repository already has one (it is kept as is);
- otherwise with your "All my repositories" settings;
- otherwise with the standard values: `master`, `develop`, `feature/`, `release/`, `hotfix/`, empty tag prefix.

When you confirm:
- the `develop` branch is created if it doesn't exist: from `master`, or from `origin/develop` if it exists on the server;
- the configuration is written to the repository with the standard `gitflow.*` keys, compatible with other git-flow tools.

> ⚠️ **The repository needs at least one commit.** On an empty repository, make a first commit on `master` first.

## 4. A feature

A feature starts from `develop` and is merged back into it. It never touches `master`.

### Start

1. In *Start a branch*, choose *Feature* and type a name, for example `login` or `ABC-123-login`.
2. *Start*: the `feature/login` branch is created from develop and checked out.

The extension refuses to start, with an explicit message, when:
- there are uncommitted changes;
- the name is invalid for git;
- the branch already exists, locally or on origin;
- a merge is in progress.

### Push (optional)

*Push* sends the branch to origin (`git push -u origin feature/login`), to share it or back it up. Only commits are pushed: commit first.

### Finish

*Finish…* opens this dialog:

```text
┌─ Finish feature/login ─────────────────────────────┐
│ Finish "feature/login"?                             │
│   • merge --no-ff into develop                      │
│   • delete the local branch                         │
│                                                     │
│ Mode   (•) Local merge   ( ) Pull Request           │
│ [ ] Push the branches (and the tag) to origin       │
│ [ ] Also delete the branch on origin                │
│                                     [OK] [Cancel]   │
└─────────────────────────────────────────────────────┘
```

With **local merge**, the default mode:

1. `--no-ff` merge of the feature into develop, so the branch stays visible in the history;
2. deletion of the local branch with `git branch -d`, which git refuses if the branch is not merged;
3. optionally, push of develop to origin and deletion of the branch on origin;
4. back to develop.

Both checkboxes can be pre-checked in the [settings](#9-settings).

## 5. Committing

The *Commit* section of the window lists the changed files: `A` added, `M` modified, `D` deleted, `R` renamed. It suggests a message built **from git only**, with no AI: the same changes always give the same message.

```text
Update 2 files

Added (1):
- src/Login.cs
Modified (1):
- README.md (+2 -1)

Refs: ABC-12
```

The last line lists the tickets found in the branch name (`ABC-123`, `#42`).

- **What gets committed**: if some files are already staged (`git add`), only those. Otherwise everything, new files included, like "Commit All".
- **The message stays editable**: once you edit it, it is no longer replaced. *Regenerate message* brings back the suggestion.
- **Rewrite the first line** for large commits: the generator knows *which* files changed, not *why*.
- **Save your files** (<kbd>Ctrl</kbd>+<kbd>S</kbd>) for them to appear: git doesn't see changes that are still only in the editor.

> ⚠️ **Refused commits**:
> - on `master`: no direct commits to production, start a feature or a hotfix;
> - during a conflicted merge: committing would conclude the merge instead of the Finish.

## 6. A release

When enough features are in develop, a release prepares them for production. On the release branch: **bug fixes, documentation and release tasks only**, no new features.

1. *Start a branch* > *Release*: the next version is suggested by incrementing the *minor* of the latest tag on master (`1.4.1` → `1.5.0`). You can change it.
2. If develop has nothing new since the last release, a warning is shown: the release would be identical to the previous one. You can continue anyway.
3. Make your final adjustments (version number, changelog…) and commit.
4. *Finish…*: the tag message is suggested (`Release 1.5.0`) and can be edited.

Finishing a release does, in this order:

1. `--no-ff` merge into `master`;
2. **annotated tag** `1.5.0` on master (or `v1.5.0` with the `v` prefix);
3. `--no-ff` merge into `develop`;
4. deletion of the branch, back to develop.

> 💡 Check "Push" to send master, develop and the tag in one go.

## 7. A hotfix

To fix production, the hotfix is the **only** branch that starts directly from `master`.

1. *Start a branch* > *Hotfix*: the suggested version is the latest tag with *patch* + 1 (`1.5.0` → `1.5.1`).
2. Fix, commit.
3. *Finish…*: merge into master with the tag, then merge into develop.

> **Hotfix during a release**: if a `release/` branch is in progress, the hotfix is merged into master and then **into the release** instead of develop. The fix reaches develop when the release is finished.

Only one release and one hotfix can be in progress at a time.

## 8. Conflicts and resuming

If a merge of the Finish hits a conflict, the extension **stops immediately and deletes nothing**. The message lists the conflicted files, and the repository stays in the merge state.

1. Resolve the conflicts, with the Visual Studio merge editor or by hand, and save.
2. Mark the files as resolved: *Stage* in Git Changes, or `git add <file>`.
3. **Don't commit yourself**: click *Resume finish…* in the window, or run *Finish…* again.

The Finish concludes the merge, skips the steps already done (merge into master, tag) and completes the rest.

> **Visual Studio already concluded the merge?** The branch shows the *already merged* badge. *Finish…* on its row creates the tag if it is missing, then deletes the branch.
> To cancel everything instead: `git merge --abort`.

## 9. Settings

Open them with the *Settings* button of the window, or from **Extensions > GitFlow > GitFlow Settings…**

```text
┌─ GitFlow Settings ──────────────────────────────────┐
│ Scope    (•) This repository  ( ) All my repositories│
│ Permanent branches                                  │
│   Production      [master        ▾]                 │
│   Development     [develop       ▾]                 │
│ Prefixes                                            │
│   Feature  [feature/]   Release [release/]          │
│   Hotfix   [hotfix/ ]   Tag     [        ]          │
│ Finish defaults                                     │
│   Mode  (•) Local merge  ( ) Pull Request           │
│   [ ] Push the branches (and the tag) to origin     │
│   [ ] Also delete the branch on origin              │
│   [ ] Pull Request: Complete (git fetch --prune)    │
│ [Reset to defaults]                           [OK]  │
└─────────────────────────────────────────────────────┘
```

- **Scope**: *This repository* (local git config) or *All my repositories* (global git config, used as defaults for new repositories).
- **Only naming is configurable**, not the behavior of the flow.
- **Tag prefix**: empty by default (tags `1.4.0`), or `v` (tags `v1.4.0`).
- **Live validation**: empty or duplicate prefix, production identical to development. Names refused by git are reported when saving, and nothing is written.
- Changing a prefix **does not rename** existing branches: a warning lists them before saving.
- **Company Azure DevOps server**: if the origin URL is not recognised, check "origin is an Azure DevOps server" ([details](pull-requests.md#company-azure-devops-server)).

All keys are described in the [reference](reference.md#configuration-keys).

## 10. Menu commands

Everything is also available from **Extensions > GitFlow**, on the open solution:

| Command | Effect |
|---|---|
| GitFlow Window | Opens the window described above. |
| Start Feature / Release / Hotfix… | Asks for the name (version pre-filled for a release or a hotfix) and creates the branch. |
| Finish Current Branch… | Finishes the current branch, or resumes a Finish stopped on a conflict. |
| Push Current Branch | `git push -u origin <branch>`. |
| GitFlow Settings… | Settings screen, or initialization screen if the repository is not initialized. |
| Initialize GitFlow… | Initialization screen. |

---

← [Home](README.md) · [Pull Request mode](pull-requests.md) →
