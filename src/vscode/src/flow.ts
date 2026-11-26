import * as vscode from 'vscode';
import { BranchType, ConfigScope, FinishContext, PrepareFinishResponse, PreparePullRequestsResponse, RepoState, Result, Settings } from './protocol';
import { GitFlowServer } from './server';
import { Ui } from './ui';

const t = vscode.l10n.t;

/**
 * The flow commands (Init, Start, Finish, Push): prompts and messages only, every rule is in the server.
 * Each command returns the result of the operation, or undefined when it was cancelled or could not start.
 */
export class FlowCommands {
  private busy = false;

  constructor(
    private readonly server: GitFlowServer,
    private readonly findRepository: () => Promise<string | undefined>,
    private readonly output: vscode.OutputChannel,
    public ui: Ui,
    private readonly onChanged: (result: Result) => void = () => undefined,
  ) {}

  /**
   * Saves the settings of the GitFlow Settings screen. On a repository that is not initialized yet, this is Init:
   * creates develop if needed and writes the naming and the Finish defaults of this repository.
   * Otherwise, in the scope of this repository, the consequences of a naming change are confirmed first.
   * Returns undefined when cancelled; a failed result keeps the screen open.
   */
  saveSettings(settings: Settings, scope: ConfigScope): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const state = await this.state(repository);
      if (!state.isInitialized) {
        const result = await this.run(t('GitFlow: initializing'), () => this.server.request<Result>('init', { repository, settings }));
        await this.show(result, t('GitFlow is initialized. Development branch: {0}.', settings.naming.developBranch));
        return result;
      }

      if (scope === 'local') {
        const warnings = await this.server.request<string[]>('getSettingsWarnings', { repository, proposed: settings.naming });
        if (warnings.length > 0 && !await this.ui.confirm(t('Save anyway?'), warnings.join('\n\n'), t('Save'))) {
          return undefined;
        }
      }

      const result = await this.run(t('GitFlow: saving settings'), () => this.server.request<Result>('saveSettings', { repository, settings, scope }));
      await this.show(result, scope === 'global'
        ? t('GitFlow settings saved for all your repositories. They are used as defaults when initializing new repositories.')
        : t('GitFlow settings saved for this repository.'));
      return result;
    });
  }

  /** Starts a feature (name asked) or a release/hotfix (next version suggested). */
  start(type: BranchType, name?: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const state = await this.state(repository);
      const naming = state.naming;
      if (!naming) {
        await this.ui.error(t('GitFlow is not initialized in this repository. Run "GitFlow: Initialize GitFlow...".'));
        return undefined;
      }

      // Reminder, not a block: a release with nothing new would be identical to the last version.
      if (type === 'release'
        && await this.server.request<number | null>('countUnreleasedCommits', { repository }) === 0
        && !await this.ui.confirm(
          t('{0} has no new commit since the last release: everything is already in {1}.', naming.developBranch, naming.masterBranch),
          t('The release would be identical to the previous one.'),
          t('Start anyway'))) {
        return undefined;
      }

      const prefix = type === 'feature' ? naming.featurePrefix : type === 'release' ? naming.releasePrefix : naming.hotfixPrefix;
      const base = type === 'hotfix' ? naming.masterBranch : naming.developBranch;
      if (!name) {
        const suggestion = await this.server.request<string>('suggestVersion', { repository, type });
        const prompt = type === 'feature'
          ? t('Feature name (branch {0}<name>, created from {1})', prefix, base)
          : type === 'release'
            ? t('Release version (branch {0}<version>, created from {1})', prefix, base)
            : t('Hotfix version (branch {0}<version>, created from {1})', prefix, base);
        name = await this.ui.input(prompt, suggestion, v => (v.trim() ? undefined : t('Enter a name.')));
        if (!name) {
          return undefined;
        }
      }

      const branch = prefix + name.trim();
      const result = await this.run(t('GitFlow: creating {0}', branch), () => this.server.request<Result>('start', { repository, type, name: name!.trim() }));
      let success = t('Branch "{0}" created from "{1}" and checked out.', result.branchName ?? branch, base);
      const reminder = type === 'release' ? (await this.state(repository)).releaseReminder : null;
      if (reminder) {
        success += ' ' + reminder;
      }

      await this.show(result, success);
      return result;
    });
  }

  /** Finishes a branch (none: the branch of a stopped merge, otherwise the current branch), with a local merge. */
  finish(branch?: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const prepared = await this.server.request<PrepareFinishResponse>('prepareFinish', { repository, branch: branch ?? null });
      if (!prepared.context) {
        await this.show(prepared.error!, '');
        return undefined;
      }

      const context = prepared.context;
      const name = context.branch.fullName;
      if (!await this.ui.confirm(this.summary(context), this.details(context), t('Finish'))) {
        return undefined;
      }

      // Local merge (default) or Pull Request, chosen at each Finish; pre-selected from the settings.
      if (context.pullRequestAvailable) {
        const platform = context.remote?.platform === 'gitHub' ? 'GitHub' : 'Azure DevOps';
        const mode = await this.ui.pickOne(t('Finish {0}: mode', name), [
          { id: 'local', label: t('Local merge'), detail: t('Merge here with --no-ff, as usual.'), picked: !context.defaults.usePullRequest },
          { id: 'pullrequest', label: t('Pull Request'), detail: t('Push the branch and open one pull request per target on {0}. Nothing is merged here.', platform), picked: context.defaults.usePullRequest },
        ]);
        if (!mode) {
          return undefined;
        }

        if (mode === 'pullrequest') {
          let prTagMessage: string | undefined;
          if (context.asksTagMessage) {
            prTagMessage = await this.ui.input(t('Message of tag "{0}"', context.tagName ?? ''), context.defaultTagMessage);
            if (prTagMessage === undefined) {
              return undefined;
            }
          }

          return this.finishWithPullRequests(repository, context, prTagMessage);
        }
      }

      let push = false;
      let deleteRemote = false;
      if (context.hasRemote) {
        const chosen = await this.ui.pickMany(t('Finish {0}: options', name), [
          { id: 'push', label: t('Push the branches (and the tag) to origin at the end'), picked: context.defaults.pushAfterFinish },
          { id: 'deleteRemote', label: t('Also delete the branch on origin'), picked: context.defaults.deleteRemoteBranch },
        ]);
        if (!chosen) {
          return undefined;
        }

        push = chosen.includes('push');
        deleteRemote = chosen.includes('deleteRemote');
      }

      let tagMessage: string | undefined;
      if (context.asksTagMessage) {
        tagMessage = await this.ui.input(t('Message of tag "{0}"', context.tagName ?? ''), context.defaultTagMessage);
        if (tagMessage === undefined) {
          return undefined;
        }
      }

      const result = await this.run(t('GitFlow: finishing {0}', name), () => this.server.request<Result>('finish', {
        repository,
        branch: name,
        tagMessage: tagMessage ?? null,
        pushAfterFinish: push,
        deleteRemoteBranch: deleteRemote,
      }));
      await this.show(result, result.tagName
        ? t('"{0}" is finished and tagged "{1}". Current branch: {2}.', name, result.tagName, result.branchName ?? '')
        : t('"{0}" is finished. Current branch: {1}.', name, result.branchName ?? ''));
      return result;
    });
  }

  /**
   * Pull Request mode: history check (nothing pushed if the branch shares no commit with a target on origin),
   * review of the generated messages, then push + one pull request per target. Nothing is merged here.
   */
  private async finishWithPullRequests(repository: string, context: FinishContext, tagMessage: string | undefined): Promise<Result | undefined> {
    const name = context.branch.fullName;
    const prepared = await this.server.request<PreparePullRequestsResponse>('preparePullRequests', { repository, branch: name });
    if (prepared.error) {
      await this.show(prepared.error, '');
      return prepared.error;
    }

    const drafts = await this.ui.reviewPullRequests({
      title: t('Pull requests for {0}', name),
      intro: t('Messages generated from git. Review and edit them if needed before they are created on {0}.', context.remote?.webUrl ?? 'origin'),
      titleLabel: t('Title'),
      descriptionLabel: t('Description'),
      create: t('Create the pull requests'),
      cancel: t('Cancel'),
    }, prepared.drafts);
    if (!drafts) {
      return undefined;
    }

    const result = await this.run(t('GitFlow: pull requests for {0}', name), () => this.server.request<Result>('finishWithPullRequests', {
      repository,
      branch: name,
      tagMessage: tagMessage ?? null,
      drafts,
    }));
    if (!result.success) {
      await this.show(result, '');
      return result;
    }

    const lines = result.pullRequests.map(pr => `• #${pr.id} → ${pr.target}: ${pr.webUrl ?? ''}`);
    if (context.tagName) {
      lines.push('', t('Tag "{0}" will be created once the PR into {1} is merged: GitFlow panel, Pull requests section.', context.tagName, context.targets[0]));
    }

    if (await this.ui.confirm(t('Pull request(s) created for "{0}":', name), lines.join('\n'), t('Open in the browser'))) {
      for (const pr of result.pullRequests) {
        if (pr.webUrl && /^https:\/\//i.test(pr.webUrl)) {
          await vscode.env.openExternal(vscode.Uri.parse(pr.webUrl));
        }
      }
    }

    return result;
  }

  /**
   * Commit from the GitFlow panel with the user's message (staged files only when something is staged).
   * No message on success, like the GitFlow window of Visual Studio: the panel shows the new state.
   */
  commit(message: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const result = await this.run(t('GitFlow: committing'), () => this.server.request<Result>('commit', { repository, message }));
      if (!result.success) {
        await this.show(result, '');
      }

      return result;
    });
  }

  /** Pull Request mode, release/hotfix: tags the merge commit of the PR to production and pushes the tag. */
  tagPullRequest(branch: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const result = await this.run(t('GitFlow: tagging {0}', branch), () => this.server.request<Result>('tagPullRequest', { repository, branch }));
      await this.show(result, t('Tag "{0}" created on the merge commit and pushed to origin.', result.tagName ?? ''));
      return result;
    });
  }

  /** Pull Request mode: all PRs merged, updates the local targets and deletes the local branch. */
  completePullRequests(branch: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const result = await this.run(t('GitFlow: completing {0}', branch), () => this.server.request<Result>('completePullRequests', { repository, branch }));
      await this.show(result, t('"{0}" is complete: its pull requests are merged and the local branch is deleted.', branch));
      return result;
    });
  }

  /** git push -u origin <branch> (none: the current branch). */
  publish(branch?: string): Promise<Result | undefined> {
    return this.exclusive(async repository => {
      const label = branch ?? (await this.state(repository)).currentBranch ?? 'HEAD';
      const result = await this.run(t('GitFlow: pushing {0}', label), () => this.server.request<Result>('publish', { repository, branch: branch ?? null }));
      await this.show(result, t('"{0}" is pushed to origin.', result.branchName ?? label));
      return result;
    });
  }

  private summary(context: FinishContext): string {
    return context.isResume
      ? t('Finishing "{0}" stopped on a conflict.', context.branch.fullName)
      : t('Finish "{0}"?', context.branch.fullName);
  }

  private details(context: FinishContext): string {
    if (context.isResume) {
      return t('If the conflicts are resolved and the files added (git add), the finish continues where it stopped.');
    }

    const lines = context.branch.type === 'feature'
      ? [t('• merge --no-ff into {0}', context.targets[0]), t('• delete the local branch')]
      : [
        t('• merge --no-ff into {0}', context.targets[0]),
        t('• tag {0}', context.tagName ?? ''),
        t('• merge --no-ff into {0}', context.targets[context.targets.length - 1]),
        t('• delete the local branch'),
      ];
    if (!context.pullRequestAvailable && context.defaults.usePullRequest) {
      lines.push('', context.pullRequestUnavailableMessage);
    }

    if (context.isWithoutCommit) {
      lines.push('', t('This branch has no commit of its own yet: there is nothing to merge.'));
    } else if (context.isAlreadyMerged) {
      lines.push('', t('This branch is already merged into all its targets: the finish will create the tag if missing, then delete the branch.'));
    }

    return lines.join('\n');
  }

  private state(repository: string): Promise<RepoState> {
    return this.server.request<RepoState>('getState', { repository });
  }

  private async run(title: string, operation: () => Promise<Result>): Promise<Result> {
    this.output.appendLine(`=== ${title} ===`);
    const result = await this.ui.progress(title, operation);
    this.output.appendLine(result.success ? 'OK' : result.message);
    this.onChanged(result);
    return result;
  }

  /** Success: a notification (with the warnings if any). Failure: the translated message, with the conflicted files. */
  private async show(result: Result, success: string): Promise<void> {
    if (result.success) {
      const warnings = result.warnings.length > 0 ? `${t('Warning:')}\n${result.warnings.map(w => '• ' + w).join('\n')}` : undefined;
      await this.ui.info(success, warnings);
      return;
    }

    const files = result.conflictedFiles.length > 0
      ? `${t('Conflicted files:')}\n${result.conflictedFiles.slice(0, 15).map(f => '• ' + f).join('\n')}${result.conflictedFiles.length > 15 ? '\n…' : ''}`
      : undefined;
    await this.ui.error(result.message, files);
  }

  /** One operation at a time; never silent on an unexpected error. */
  private async exclusive(action: (repository: string) => Promise<Result | undefined>): Promise<Result | undefined> {
    if (this.busy) {
      await this.ui.info(t('A GitFlow operation is already running. Wait for it to finish.'));
      return undefined;
    }

    this.busy = true;
    try {
      const repository = await this.findRepository();
      if (!repository) {
        await this.ui.error(t('No Git repository found for the open folder or file.'));
        return undefined;
      }

      return await action(repository);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.output.appendLine(error instanceof Error && error.stack ? error.stack : message);
      await this.ui.error(t('GitFlow: unexpected error. {0}', message));
      return undefined;
    } finally {
      this.busy = false;
    }
  }
}
