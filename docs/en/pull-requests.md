# Pull Request mode

[Home](README.md) · [Guide](guide.md) · **Pull Request mode** · [Reference and troubleshooting](reference.md) · [Français](../pull-requests.md)

Pull Request mode is a Finish option for repositories where `master` and `develop` are protected. Instead of merging locally, the extension pushes the branch and opens the pull requests. Review and merge stay in human hands.

**Contents**

1. [When to use it](#when-to-use-it)
2. [Requirements](#requirements)
3. [A feature through a PR](#a-feature-through-a-pr)
4. [The generated message](#the-generated-message)
5. [Tracking PRs](#tracking-prs)
6. [Release and hotfix: the tag](#release-and-hotfix-the-tag)
7. [Complete branch](#complete-branch)
8. [GitHub and Azure DevOps](#github-and-azure-devops)

---

## When to use it

| | Local merge (default) | Pull Request (option) |
|---|---|---|
| Branches protected on the server | Push refused | ✅ Designed for it |
| Code review | — | ✅ On the platform |
| Speed | ✅ Immediate | After the PR is approved |
| Remote required | No (or any remote) | GitHub or Azure DevOps |

You choose **at each Finish**, in the *Finish* dialog. To pre-select *Pull Request* on a repository, set "Default Finish mode" in the [settings](guide.md#9-settings).

## Requirements

- The `origin` remote points to **GitHub** (`github.com`) or **Azure DevOps** (`dev.azure.com` or `*.visualstudio.com`), over https or ssh.
- **Git already knows your credentials**. That is the case if you have already pushed or fetched over https on this server, from Visual Studio or a terminal.
  - The extension asks git for them (`git credential fill`, in practice the Git Credential Manager).
  - It stores none, and they never appear in the logs.
- **No extra tool**: no `gh`, no `az`.

> ⚠️ **"No git credentials for dev.azure.com"**
> Sign in once: run `git ls-remote <repository url>` in a terminal, and the Git Credential Manager sign-in window opens. Then try again.

## A feature through a PR

1. Start and develop the feature as usual ([guide](guide.md#4-a-feature)).
2. *Finish…*: choose **Pull Request**. The push and delete-on-origin options disappear: they are not used in PR mode.
3. The next screen shows the **generated title and description** for each PR. You can edit them.
4. OK: the extension pushes the branch, then creates the PR `feature/x` → `develop`. If one is already open, it is reused, and your new commits go into it. Nothing is merged locally.
5. Get it reviewed, then **merge the PR on the platform**. Prefer a merge without fast-forward (see [squash](#complete-branch)).
6. In the GitFlow window: *Refresh PRs*, then *Complete branch*.

## The generated message

It is built from git only: deterministic, no AI. The title follows the convention `Feature: name` / `Release 1.5.0` / `Hotfix 1.5.1`. The description:

```markdown
**Feature** `feature/ABC-12-login` → `develop`

### Commits (2)
- Add the login page
- Fix the button style (#42)

### Files
5 file(s) changed, 120 line(s) added, 8 line(s) deleted

### Tickets
ABC-12, #42
```

For a release or a hotfix, a *Version* section reminds you of the tag that will be created after the merge.

### Customizing

- **Your template**: `.gitflow/pull_request_template.md` in the repository, with the variables `{type}` `{source}` `{target}` `{commits}` `{stats}` `{version}` `{tickets}`.
- **The platform template** (`.github/pull_request_template.md`, `.azuredevops/pull_request_template.md`…) is used as a base: the generated description is added below it.
- **Ticket pattern**: the `gitflowvs.pr.ticketpattern` key (regular expression). Default: `ABC-123` and `#42`.

## Tracking PRs

A *Pull requests* section appears in the window as soon as a PR is tracked:

```text
Pull requests                               [Refresh PRs]
release/1.5.0
   #12 → master    merged                           [Open]
   #13 → develop   open                             [Open]
                                       [Create tag 1.5.0]
```

- Each PR has its row, with its state (*open*, *merged*, *closed without merge*) and an *Open* button for the browser.
- The state is read from the platform **at most once a minute** while the window is visible, or immediately with *Refresh PRs*.
- The extension **never approves or merges** a PR.

## Release and hotfix: the tag

A release opens **two PRs**: to `master` and to `develop`. So does a hotfix: to master, then to develop (or to the release in progress).

The tag can only be created **once the PR to master is merged**, on its merge commit:

1. Merge the PR to master on the platform.
2. *Refresh PRs*: the *Create tag 1.5.0* button appears.
3. The extension:
   - fetches master;
   - checks that the merge commit is on `origin/master`;
   - creates the annotated tag, with the message entered at Finish;
   - pushes the tag.

*Complete branch* also creates the tag if it is missing, when both PRs are already merged.

## Complete branch

When **all** the PRs of a branch are merged, *Complete branch*:

1. creates the tag if it is a release or a hotfix and the tag is missing;
2. runs `git fetch`, or `git fetch --prune` if the option is checked in the settings;
3. updates your local `develop` and `master`, fast-forward only;
4. deletes the local branch with `git branch -d`, and forgets its PRs;
5. leaves you on develop, or on the release in progress.

> ⚠️ **"Squash" or "rebase" merge**: the commits of your branch are then not in develop. The extension detects it and **does not delete** the branch. Delete it yourself if you are sure. To avoid this, choose a regular merge: *Merge*, or *Merge (no fast-forward)*.

> 💡 **Branches on the server**: Complete does not delete the branch on the server. For that:
> - on Azure DevOps, check *Delete branch after merging*;
> - on GitHub, enable *Automatically delete head branches*.
>
> With the *git fetch --prune* option, the matching `origin/…` references then disappear from your machine on their own.

## GitHub and Azure DevOps

| | GitHub | Azure DevOps |
|---|---|---|
| Remote URL | `https://github.com/org/repo.git`<br>`git@github.com:org/repo.git` | `https://dev.azure.com/org/project/_git/repo`<br>`git@ssh.dev.azure.com:v3/org/project/repo`<br>`https://org.visualstudio.com/project/_git/repo` |
| Credentials | git's credentials for `github.com` | git's credentials for `dev.azure.com` (Microsoft account or personal access token) |
| Protected branches | Branch protection rules | Branch policies |
| Delete the branch after merge | *Delete branch* on the PR, or repository setting | *Delete … after merging* checkbox on Complete |

With an `ssh` remote, the extension still asks the Git Credential Manager for https credentials to call the API. Sign in once over https if needed.

---

← [Guide](guide.md) · [Reference and troubleshooting](reference.md) →
