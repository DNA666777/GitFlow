# GitFlow for VS Code

[Home](README.md) · [Guide](guide.md) · [Pull Request mode](pull-requests.md) · [Reference and troubleshooting](reference.md) · **VS Code** · [Français](../vscode.md)

The same GitFlow extension, in **Visual Studio Code** (Windows, Linux, macOS). The engine is the same as in Visual Studio: same rules, same git commands, same messages. Only the interface adapts to VS Code.

**Contents**

1. [Installation](#1-installation)
2. [The GitFlow panel](#2-the-gitflow-panel)
3. [Commands](#3-commands)
4. [Settings and initialization](#4-settings-and-initialization)
5. [Finishing a branch](#5-finishing-a-branch)
6. [The log](#6-the-log)
7. [Troubleshooting](#7-troubleshooting)

---

## 1. Installation

- **From VS Code**: *Extensions* view (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>X</kbd>), search **GitFlow** (publisher DNA666777), *Install*.
- **From a file**: download `gitflow-for-vscode-<version>.vsix` from the [releases page](https://github.com/DNA666777/GitFlow/releases/latest), then:

  ```powershell
  code --install-extension gitflow-for-vscode-<version>.vsix
  ```

Requirements:

- VS Code 1.90 or later;
- `git` on the PATH;
- the **.NET 8 runtime**, provided automatically: Microsoft's *.NET Install Tool* extension is installed with GitFlow and downloads .NET 8 once (a few dozen MB) into VS Code's folder. Nothing to install by hand.

## 2. The GitFlow panel

Open **Source Control** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd>): the **GitFlow** section mirrors the GitFlow window of Visual Studio, in the same order.

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

- **Repository state**: current branch and its type, changes, ahead of or behind origin.
- **Commit**: the changed files and a message generated from git (as in Visual Studio). The message is yours once you edit it; *Regenerate message* brings back the suggestion; <kbd>Ctrl</kbd>+<kbd>Enter</kbd> commits.
- **Start a branch**: type, prefix, name; the next version is pre-filled for a release or a hotfix.
- **Branches in progress** with *Push* and *Finish…* on each row. Badges: *current*, *already merged*, *no commit yet*.
- **Pull requests**, when some are followed: state, *Open*, *Create tag*, *Complete branch*.

The panel refreshes on its own (commit, branch change, saved file…), only while it is visible. The ↻ button of the title forces a refresh.

## 3. Commands

Command palette (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>P</kbd>), **GitFlow** category:

| Command | Effect |
|---|---|
| GitFlow: Initialize GitFlow... | Initialization screen (see below). |
| GitFlow: Start Feature... / Start Release... / Start Hotfix... | Asks for the name (version pre-filled for release/hotfix) and creates the branch. |
| GitFlow: Finish Current Branch... | Finishes the current branch, or resumes a Finish stopped on a conflict. |
| GitFlow: Push Current Branch | `git push -u origin <branch>`. |
| GitFlow: GitFlow Settings... | Settings screen. |
| GitFlow: Show Log / Restart Server | Log; restart of the engine. |

Labels follow the VS Code display language (English or French).

## 4. Settings and initialization

*Settings* in the panel (or the gear of the title) opens the **GitFlow Settings** tab, with the same fields as in Visual Studio:

- scope: *this repository* or *all my repositories*;
- production and development branches, prefixes, tag prefix;
- Finish defaults: Local merge or Pull Request mode, push, delete on origin, `fetch --prune`, company Azure DevOps server.

Errors (empty or duplicate prefix, production = development, name refused by git) show up while typing and block saving.

On a repository that is not initialized yet, the same screen is called **Initialize GitFlow**: check the names, then *Initialize* creates `develop` if needed and writes the configuration.

## 5. Finishing a branch

*Finish…* goes through:

1. a confirmation that sums up what will happen (refused at once when there are uncommitted changes);
2. the **mode**: *Local merge* or *Pull Request* (when `origin` is on GitHub or Azure DevOps);
3. with a local merge, the options: push to origin, delete the branch on origin;
4. for a release or a hotfix, the tag message.

With **Pull Request**, a tab shows the generated title and description of each PR, editable, then *Create the pull requests* pushes the branch and creates the PRs. They are followed in the *Pull requests* section of the panel, exactly as in Visual Studio: see [Pull Request mode](pull-requests.md).

## 6. The log

**GitFlow: Show Log** (or *Log* in the panel) opens the **GitFlow** channel of the *Output* view: each operation and its git commands, with the exit code.

The reads done to refresh the panel are logged too, at the *Trace* level, hidden by default. To see them: *Output: Set Log Level...* > *Trace*, on the GitFlow channel.

## 7. Troubleshooting

**"The .NET 8 runtime could not be obtained"**: look at the *.NET Install Tool* output (proxy, network…). You can also point to an existing `dotnet` with the user setting `gitflow.dotnetPath` (File > Preferences > Settings, search "gitflow").

**The panel shows "GitFlow could not start"**: *Log* for the details, then **GitFlow: Restart Server**.

**Company Azure DevOps Server on Linux or macOS**: without a git credential, Windows integrated authentication depends on the Kerberos setup of the machine. Prefer a personal access token stored by the Git Credential Manager.

---

← [Reference and troubleshooting](reference.md) · [Home](README.md)
