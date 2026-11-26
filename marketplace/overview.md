# GitFlow for Visual Studio

**The standard Gitflow, without leaving Visual Studio.** Start and finish your `feature/`, `release/` and `hotfix/` branches, strictly following the [Atlassian Gitflow Workflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow).

**English and French**: the extension follows the Visual Studio language.

**Also for VS Code**: the same extension, same engine, for Visual Studio Code — [GitFlow for VS Code](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow-for-vscode).

📖 **Tutorial**: https://dna666777.github.io/GitFlow/en/ ([en français](https://dna666777.github.io/GitFlow/))

## Features

- **The flow**
  - **Init**: creates `develop` and writes the standard git-flow config.
  - **Start**: next version suggested for a release or a hotfix.
  - **Finish**: `--no-ff` merges, annotated tag. A hotfix during a release is merged into the release.
  - **Push**: pushes the branch to origin.
- **GitFlow window**, as a tab next to *Git Changes*: repository state, branches in progress with their *Push* and *Finish* buttons, reminder of the rules on a release branch.
- **Commit**: list of changes and a commit message **generated from git** (files, lines added/deleted, tickets from the branch name), editable.
- **Pull Request mode (option)** for repositories where `master` and `develop` are protected, on **GitHub** and **Azure DevOps**: generated PR message, PR tracking in the window, tag created on the merge commit, branch completion.
- **Settings**: branch names, prefixes, tag prefix, Finish defaults, for one repository or for all your repositories.

## Safety

- Start and Finish refused when some changes are not committed. Never an automatic stash.
- On a merge conflict: immediate stop, nothing deleted, and the Finish resumes after resolution.
- Never `push --force`, `reset --hard`, or deletion of an unmerged branch.
- Every git command run is visible in *Output > GitFlow*.

## Dependencies

Only Visual Studio and Git for Windows. The extension needs neither git-flow, nor `gh`, nor `az`. Pull Request mode reuses the credentials git already knows, through the Git Credential Manager, and the extension stores none.

## Requirements

- Visual Studio 2022 **17.14** or later, or Visual Studio 2026.
- Git for Windows, installed with Visual Studio.

## Getting started

1. **Extensions > GitFlow > GitFlow Window**.
2. **Initialize GitFlow…**, then confirm the suggested names.
3. **Start a branch** > *Feature*, then work and commit.
4. **Finish…**, with *Local merge* or *Pull Request*.

The rest is in the [guide](https://dna666777.github.io/GitFlow/en/guide.html). To report a problem or follow the releases: [GitHub](https://github.com/DNA666777/GitFlow).

License [MIT](https://github.com/DNA666777/GitFlow/blob/master/LICENSE).
