# Reference and troubleshooting

[Home](README.md) · [Guide](guide.md) · [Pull Request mode](pull-requests.md) · **Reference and troubleshooting** · [Français](../reference.md)

The rules applied, the configuration keys and answers to common problems.

**Contents**

1. [The model in short](#the-model-in-short)
2. [Safety rules](#safety-rules)
3. [Configuration keys](#configuration-keys)
4. [Git commands run](#git-commands-run)
5. [Troubleshooting](#troubleshooting)
6. [Compatibility](#compatibility)

---

## The model in short

The extension strictly follows the [Atlassian Gitflow Workflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow), with no variant. One difference: the production branch is called `master` by default. This name is configurable.

| Type | Prefix | Starts from | Finishes into | Tag | Suggested version |
|---|---|---|---|---|---|
| Feature | `feature/` | develop | develop | no | — |
| Release | `release/` | develop | master **then** develop | yes | minor + 1 (`1.4.1` → `1.5.0`) |
| Hotfix | `hotfix/` | master | master **then** develop (or the release in progress) | yes | patch + 1 (`1.5.0` → `1.5.1`) |

- There is no other branch type (no `bugfix/`, `support/`…).
- Merges use `--no-ff`, with no squash or rebase.
- Tags are annotated, in the `{prefix}{version}` format. Their default message is `Release x.y.z` / `Hotfix x.y.z`, and it can be edited.
- Only one release and one hotfix can be in progress at a time.

## Safety rules

| | |
|---|---|
| Uncommitted changes | Start and Finish are refused. The extension never stashes automatically: commit or run `git stash` yourself. Untracked files don't block. |
| Checks before acting | Base branch exists, name valid for git (`git check-ref-format`), target branch doesn't exist, tag doesn't exist. |
| Merge conflict | Immediate stop, nothing deleted, can resume after resolution. |
| Forbidden commands | Never `push --force`, `reset --hard` or `branch -D`. An unmerged branch is never deleted. |
| Commits | Refused on the production branch and during a merge. |
| Pull Requests | Created and tracked, never approved or merged by the extension. The tag is only created on a merge commit verified on `origin/master`. |
| Credentials | Asked from git each time, never stored or written to the logs. |

## Configuration keys

They are stored in the repository git config (`--local`) or the user git config (`--global`). Global values are used as defaults for new repositories. The local value wins.

### Naming (standard git-flow keys)

| Key | Default | Purpose |
|---|---|---|
| `gitflow.branch.master` | `master` | Production branch |
| `gitflow.branch.develop` | `develop` | Integration branch |
| `gitflow.prefix.feature` | `feature/` | Feature prefix |
| `gitflow.prefix.release` | `release/` | Release prefix |
| `gitflow.prefix.hotfix` | `hotfix/` | Hotfix prefix |
| `gitflow.prefix.versiontag` | *(empty)* | Version tag prefix (`v` → `v1.4.0`) |

### Extension options

| Key | Default | Purpose |
|---|---|---|
| `gitflowvs.finish.mode` | `local` | Mode pre-selected at Finish: `local` or `pullrequest` |
| `gitflowvs.finish.push` | `false` | Pre-checks "Push" at local Finish |
| `gitflowvs.finish.deleteremote` | `false` | Pre-checks "Also delete the branch on origin" |
| `gitflowvs.pr.prune` | `false` | *Complete branch* uses `git fetch --prune` |
| `gitflowvs.pr.ticketpattern` | ABC-123 and #42 | Regular expression for ticket references in PRs |
| `gitflowvs.pr.platform` | `auto` | `azuredevops`: origin is a company Azure DevOps server (URL not recognised automatically) |

Tracked PRs are stored in the local config, under `gitflowvs-pr.<branch>.link`. They are removed when the branch is completed.

Example, to switch a repository to the `v` tag prefix:

```powershell
git config --local gitflow.prefix.versiontag v
```

## Git commands run

All commands go through the `git` of Git for Windows: your hooks, GPG signatures, LFS and credentials apply just like in a terminal. They are visible in **View > Output**, **GitFlow** channel:

```text
=== GitFlow: finishing feature/login ===
[10:42:03] git switch develop  → 0 (81 ms)
[10:42:03] git merge --no-ff -m "Merge branch 'feature/login' into develop" feature/login  → 0 (153 ms)
[10:42:03] git branch -d feature/login  → 0 (54 ms)
OK
```

## Troubleshooting

### The GitFlow menu doesn't appear

Check **Extensions > Manage Extensions > Installed**. If the extension is not there, reinstall the `.vsix` **with Visual Studio closed**. If it is open, the Visual Studio Installer closes without installing anything.

### A click does nothing

Look at the **GitFlow** channel of the Output window, then at the extension log: `%TEMP%\servicehub\logs\*GitFlow.VSExtension*.log`.

### "No Git repository found"

The extension looks for the repository in this order: the open solution, then the selected item, then the active document. Open a solution located in a Git repository.

### "Some changes are not committed"

Commit (Commit section) or run `git stash`, then try again. Untracked files don't count.

### My changed files don't appear in Commit

Save them (<kbd>Ctrl</kbd>+<kbd>S</kbd>): git only sees files written to disk.

### The suggested release version already has a tag

The next version is computed from the highest version tag on master. Type another version: a version whose tag already exists is refused.

### "Pull Request mode unavailable"

The `origin` remote is neither GitHub nor Azure DevOps (a local folder, GitLab, Bitbucket…): use local merge. Check the URL with `git remote -v`. For a company Azure DevOps server, check the matching option in the settings ([details](pull-requests.md#company-azure-devops-server)).

### "The platform rejected the request"

The message from GitHub or Azure DevOps is shown as is: insufficient rights, PR already exists, missing target branch… After fixing it, run *Finish…* again: an already open PR is reused.

### Complete refuses: branch not contained in develop

The PR was merged with *squash* or *rebase*: the branch commits are not in develop, so nothing is deleted. If the content was delivered, delete the branch yourself: `git branch -D feature/x`.

## Compatibility

| | |
|---|---|
| Visual Studio | 2022 version 17.14 or later, 2026 (18.x). Community, Professional, Enterprise editions. |
| Git | Git for Windows 2.28 or later (installed with Visual Studio) |
| Platforms (PR mode) | GitHub (github.com), Azure DevOps (dev.azure.com, *.visualstudio.com), Azure DevOps Server / TFS 2018 or later (declared in the settings) |
| Languages | English (default) and French, following the Visual Studio language. Generated messages (commit, PR) use the same language. |
| Configuration | Compatible with the `gitflow.*` keys of git-flow / git-flow-avh |

---

← [Pull Request mode](pull-requests.md) · [Home](README.md)
