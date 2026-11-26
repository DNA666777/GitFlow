# GitFlow for VS Code

The **standard Gitflow** ([Atlassian Gitflow Workflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow)) without leaving VS Code: start and finish `feature/`, `release/` and `hotfix/` branches, with a local merge or Pull Requests on **GitHub** and **Azure DevOps**.

The same engine as the [GitFlow extension for Visual Studio](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow): both editors behave exactly the same way. **English and French** (follows the VS Code display language).

📖 **Tutorial**: https://dna666777.github.io/GitFlow/en/vscode.html ([en français](https://dna666777.github.io/GitFlow/vscode.html))

## Features

- **GitFlow panel** in the Source Control view, with the sections of the Visual Studio window:
  - repository state (branch, type, changes, ahead/behind);
  - **Commit**: changed files and a commit message **generated from git** (files, lines added/deleted, tickets from the branch name), editable;
  - **Start a branch**: the next version is suggested for a release or a hotfix;
  - **branches in progress** with *Push* and *Finish…*;
  - **pull requests** followed, with *Create tag* and *Complete branch*.
- **The flow**: Init (creates `develop`, standard `gitflow.*` configuration), Start, Finish (`--no-ff` merges, annotated tag, a hotfix during a release goes into the release), Push.
- **Pull Request mode (option)** for protected branches: generated messages reviewed before creation, tag created on the merge commit, branch completed once all PRs are merged. GitHub, Azure DevOps and company Azure DevOps servers.
- **Settings**: branch names, prefixes, tag prefix, Finish defaults, for one repository or all your repositories, with live validation.
- **Safety**: no Start/Finish with uncommitted changes (never an automatic stash), stop on merge conflicts with resume, never `push --force`, `reset --hard` or deletion of an unmerged branch, every git command in the *GitFlow* log.

## Requirements

- VS Code 1.90 or later, `git` on the PATH.
- The .NET 8 runtime is provided automatically by the **.NET Install Tool** extension (installed with this one). To use another `dotnet`, set `gitflow.dotnetPath`.
- Pull Request mode: the credentials git already uses (Git Credential Manager). No `gh`, no `az`, no token stored by the extension.

## Getting started

1. Open a folder that contains a Git repository, then the **Source Control** view: section **GitFlow**.
2. **Initialize GitFlow…**, check the suggested names, *Initialize*.
3. **Start a branch** > *Feature*, work and commit from the panel.
4. **Finish…** on the branch row, with *Local merge* or *Pull Request*.

[Source code and issues](https://github.com/DNA666777/GitFlow) · License MIT
