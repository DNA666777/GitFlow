# GitFlow for Visual Studio and VS Code

🌐 **English** · [Français](README.fr.md)

Extension for **Visual Studio** and **Visual Studio Code** to apply the **standard Gitflow** without leaving the editor. It starts and finishes `feature/`, `release/` and `hotfix/` branches, strictly following the [Atlassian Gitflow Workflow](https://www.atlassian.com/git/tutorials/comparing-workflows/gitflow-workflow).

> 🌐 **Tutorial website**: https://dna666777.github.io/GitFlow/en/
>
> 📖 **Markdown tutorial**: [Home](docs/en/README.md) · [Guide](docs/en/guide.md) · [Pull Request mode](docs/en/pull-requests.md) · [Reference and troubleshooting](docs/en/reference.md) · [VS Code](docs/en/vscode.md)
>
> ⬇️ **Download**: Visual Studio — [Marketplace](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow) · VS Code — [Marketplace](https://marketplace.visualstudio.com/items?itemName=DNA666777.gitflow-for-vscode) · both — [latest release](https://github.com/DNA666777/GitFlow/releases/latest) (`.vsix` files)

## Features

- **The flow**:
  - **Init**: creates `develop` and writes the standard git-flow config (`gitflow.*`).
  - **Start**: the next version is suggested for a release or a hotfix.
  - **Finish**: `--no-ff` merges, annotated tag for a release or a hotfix. A hotfix started during a release is merged into that release.
  - **Push**: pushes the branch to `origin`.
- **GitFlow window**, as a tab next to *Git Changes*:
  - repository state and branches in progress, with their Push and Finish buttons;
  - "bug fixes, docs and release tasks only" reminder on a release branch.
- **Commit**: list of changes and a commit message **generated from git** (files, lines added/deleted, tickets from the branch name), editable.
- **Pull Request mode (option)**, for repositories where `master` and `develop` are protected, on **GitHub** and **Azure DevOps**:
  - generated PR message;
  - PR tracking in the window;
  - tag created on the merge commit;
  - branch completion.
- **Settings**: naming (branches, prefixes, tag prefix), Finish defaults, scope *this repository* or *all my repositories*.
- **Safety**:
  - Start and Finish refused when there are uncommitted changes (no automatic stash);
  - stops on conflicts, and the Finish can resume;
  - never `push --force`, `reset --hard`, or deletion of an unmerged branch;
  - every git command is logged in *Output > GitFlow*.
- **English and French**: the extension follows the Visual Studio language (French for a French VS, English otherwise). Generated commit and PR messages use the same language.
- **Dependencies**: only Visual Studio and Git for Windows. No git-flow, no `gh`, no `az`: PR mode reuses the credentials git already knows (Git Credential Manager), and stores none.

## VS Code

The same extension exists for **Visual Studio Code** (Windows, Linux, macOS), with the same engine: a GitFlow panel in the Source Control view (same sections as the Visual Studio window, Commit included), the commands in the palette, the settings screen and the Pull Request mode. The .NET 8 runtime is provided automatically by the *.NET Install Tool* extension. See [GitFlow for VS Code](docs/en/vscode.md).

## Requirements

| | |
|---|---|
| Visual Studio | 2022 **17.14+** or 2026 |
| Git | Git for Windows (installed with Visual Studio), `git` on the PATH |
| Pull Request mode | `origin` remote on GitHub or Azure DevOps, git credentials already set up |

## Installation

1. **Close Visual Studio.**
2. Install `GitFlow.VSExtension.vsix`: double-click it, or from the command line:
   ```powershell
   & "<VS folder>\Common7\IDE\VSIXInstaller.exe" GitFlow.VSExtension.vsix
   ```
3. Restart Visual Studio: the **Extensions > GitFlow** menu appears.

To build the `.vsix` yourself, see [Building](#building) below.

## Quick start

1. Open a solution located in a Git repository, then **Extensions > GitFlow > GitFlow Window**.
2. **Initialize GitFlow…** and confirm the suggested names (`master`, `develop`, `feature/`, `release/`, `hotfix/`).
3. **Start a branch**: *Feature*, name `login`, then *Start*.
4. Work, then **Commit** from the Commit section.
5. **Finish…** on the branch row, with *Local merge* or *Pull Request*.

The rest is in the [guide](docs/en/guide.md).

## The model

| Type | Prefix | Starts from | Finishes into | Tag |
|---|---|---|---|---|
| feature | `feature/` | develop | develop | no |
| release | `release/` | develop | master **then** develop | yes |
| hotfix | `hotfix/` | master | master **then** develop (or the release in progress) | yes |

Only naming is configurable. The behavior of the flow is not, and there is no other branch type.

## Building

```powershell
# Full build: also produces the .vsix (Visual Studio MSBuild not required)
dotnet build GitFlowVS.sln -c Debug
#   → src/GitFlow.VSExtension/bin/Debug/net8.0-windows8.0/GitFlow.VSExtension.vsix

# Tests
dotnet test tests/GitFlow.Core.Tests               # unit tests (simulated git)
dotnet test tests/GitFlow.Core.IntegrationTests    # real git repositories in %TEMP% (a few minutes)
```

After changing `<Version>` in `GitFlow.VSExtension.csproj`, rebuild with `--no-incremental`: otherwise the `.vsix` keeps the old version.

### Architecture

```
GitFlowVS.sln
├── src/
│   ├── GitFlow.Core/            # All the logic, plain .NET, no reference to Visual Studio
│   │   ├── Git/                 # IGitRunner (the only gateway to git), GitCliRunner, GitClient
│   │   ├── Flow/                # FlowService, LocalMergeStrategy, CommitService, message generator
│   │   ├── Config/              # gitflow.* config and gitflowvs.* options
│   │   ├── PullRequests/        # PR mode: GitHub, Azure DevOps, credentials through git
│   │   ├── Model/               # FlowResult, RepoState, BranchType…
│   │   └── Resources/           # Texts: Strings.resx (English), Strings.fr.resx (French)
│   └── GitFlow.VSExtension/     # Thin VS layer (VisualStudio.Extensibility, out-of-process, Remote UI)
├── tests/
│   ├── GitFlow.Core.Tests/              # Unit tests
│   └── GitFlow.Core.IntegrationTests/   # Tests on real temporary git repositories
└── docs/                        # Tutorial (Markdown + small static site): French, en/ for English
```

- **Stack**:
  - .NET 8, C#;
  - `Microsoft.VisualStudio.Extensibility` SDK 17.14 (out-of-process extension);
  - command-line `git`, to respect the user's config: credentials, hooks, GPG signatures, LFS;
  - xUnit + Shouldly for tests.
- **Golden rule**: all the logic is in `GitFlow.Core`. The VS extension only displays, collects the user's choices and calls the Core. A bug that can be reproduced without VS must be reproduced by a Core test.

## Releasing a version

Publishing is automated (GitHub Actions) and follows the flow:

1. **Start a release** with the extension (suggested version, for example `1.4.0`).
2. **Finish** the release with "Push" checked, or in Pull Request mode then *Create tag*. The `1.4.0` tag reaches GitHub.
3. The [`release.yml`](.github/workflows/release.yml) workflow runs on this tag:
   - Release build, **at the tag version**;
   - unit and integration tests;
   - package version check;
   - **GitHub Release** with `GitFlow.VSExtension-1.4.0.vsix` attached and notes generated from the commits.

To (re)publish an existing tag: *Actions* tab > *Release* > *Run workflow*, then enter the tag.

**Visual Studio Marketplace**: if the `VS_MARKETPLACE_PAT` secret is set in the repository, the release is also published to the Marketplace (listing described by [`marketplace/publishManifest.json`](marketplace/publishManifest.json) and [`marketplace/overview.md`](marketplace/overview.md)). Without this secret, the step is skipped.

The [`ci.yml`](.github/workflows/ci.yml) workflow builds and tests every push to `develop` or `master` and every pull request. The `.vsix` of each build is kept for 7 days as an artifact.

## License

[MIT](LICENSE) — © 2026 DNA666777.

## Documentation

The tutorial is in [`docs/en/`](docs/en/README.md) in English and in [`docs/`](docs/README.md) in French, each in two formats with the same content:

- **Markdown**, readable directly on GitHub: [home](docs/en/README.md), [guide](docs/en/guide.md), [Pull Request mode](docs/en/pull-requests.md), [reference](docs/en/reference.md).
- **Small static website**, no build and no dependency, published at **https://dna666777.github.io/GitFlow/en/** by GitHub Pages
  (`master` branch, `/docs` folder: the site is updated at each release). Locally, open `docs/en/index.html` in a browser.
