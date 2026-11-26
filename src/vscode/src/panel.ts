import * as crypto from 'node:crypto';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { FlowCommands } from './flow';
import { BranchType, CommitPreview, PullRequestGroup, RepoState, Result } from './protocol';
import { Refresher } from './refresher';
import { GitFlowServer } from './server';

const t = vscode.l10n.t;

/** Everything the webview displays: built here (data from the server, translated texts), only rendered there. */
export interface PanelModel {
  kind: 'loading' | 'noRepository' | 'notInitialized' | 'ready' | 'error';
  repositoryName: string;
  status: string;
  labels: Record<string, string>;
  state?: {
    branch: string;
    type: string;
    workingTree: string;
    sync: string;
    releaseReminder: string | null;
    pendingFinish: { branch: string; text: string } | null;
  };
  commit?: {
    header: string;
    changes: { status: string; path: string; title: string }[];
    modeText: string;
    suggestedMessage: string;
    blocked: string;
  };
  start: { type: BranchType; label: string; prefix: string; suggestion: string }[];
  groups: { label: string; branches: { fullName: string; name: string; badge: string }[] }[];
  pullRequests: {
    source: string;
    items: { label: string; state: string; url: string | null }[];
    tagLabel: string;
    canTag: boolean;
    canComplete: boolean;
  }[];
}

/** Messages from the webview (a click or a submitted field); the panel calls the flow commands. */
type PanelMessage =
  | { type: 'refresh' | 'init' | 'refreshPullRequests' | 'showLog' }
  | { type: 'start'; branchType: BranchType; name: string }
  | { type: 'finish' | 'publish' | 'tag' | 'complete'; branch: string }
  | { type: 'commit'; message: string }
  | { type: 'openUrl'; url: string };

/**
 * The "GitFlow" panel of the Source Control view: the same sections, in the same order, as the GitFlow window
 * of Visual Studio (state, Commit, Pull requests, Start a branch, Branches in progress, status line).
 */
export class GitFlowPanel implements vscode.WebviewViewProvider, vscode.Disposable {
  readonly refresher = new Refresher(() => void this.update());
  private view: vscode.WebviewView | undefined;
  private status = '';
  private lastModel: PanelModel | undefined;
  private refreshPullRequests = false;

  constructor(
    private readonly extensionUri: vscode.Uri,
    private readonly server: GitFlowServer,
    private readonly flow: FlowCommands,
    private readonly findRepository: () => Promise<string | undefined>,
    private readonly output: vscode.LogOutputChannel,
  ) {}

  /** Last model sent to the webview (tests). */
  get model(): PanelModel | undefined {
    return this.lastModel;
  }

  /** After each operation: its outcome on the status line, then a refresh. */
  onResult(result: Result): void {
    this.status = result.success
      ? (result.warnings.length > 0 ? t('Done, with warnings.') : t('Done.'))
      : result.message;
    this.refresher.refresh(true);
  }

  resolveWebviewView(view: vscode.WebviewView): void {
    this.view = view;
    const media = vscode.Uri.joinPath(this.extensionUri, 'media');
    view.webview.options = { enableScripts: true, localResourceRoots: [media] };
    view.webview.html = this.html(view.webview, media);
    view.webview.onDidReceiveMessage((message: PanelMessage) => void this.handle(message));
    view.onDidDispose(() => (this.view = undefined));
    this.refresher.attach(view);
    this.refresher.refresh();
  }

  dispose(): void {
    this.refresher.dispose();
  }

  /** Builds the model from the server (also used by the tests). */
  async buildModel(): Promise<PanelModel> {
    const labels = panelLabels();
    const empty = (kind: PanelModel['kind'], repositoryName = ''): PanelModel =>
      ({ kind, repositoryName, status: this.status, labels, start: [], groups: [], pullRequests: [] });

    let repository: string | undefined;
    try {
      repository = await this.findRepository();
      if (!repository) {
        return empty('noRepository');
      }

      const state = await this.server.request<RepoState | null>('getState', { repository });
      const name = path.basename(repository);
      if (!state) {
        return empty('noRepository');
      }

      if (!state.isInitialized || !state.naming) {
        return empty('notInitialized', name);
      }

      const naming = state.naming;
      const [preview, release, hotfix, prs] = await Promise.all([
        this.server.request<CommitPreview | null>('getCommitPreview', { repository }),
        this.server.request<string>('suggestVersion', { repository, type: 'release' }),
        this.server.request<string>('suggestVersion', { repository, type: 'hotfix' }),
        this.server.request<PullRequestGroup[]>('getPullRequests', { repository, refresh: this.refreshPullRequests }),
      ]);
      this.refreshPullRequests = false;
      const prError = prs.flatMap(g => g.pullRequests).find(p => p.error)?.error;

      return {
        kind: 'ready',
        repositoryName: name,
        status: prError ?? this.status,
        labels,
        state: {
          branch: state.currentBranch ?? t('(detached HEAD)'),
          type: typeLabel(state.currentFlowBranch?.type),
          workingTree: state.isMergeInProgress ? t('Merge in progress') : state.isDirty ? t('Uncommitted changes') : t('Clean'),
          sync: !state.hasRemote ? t('No origin remote')
            : state.ahead === null || state.behind === null ? t('Branch not pushed yet')
              : t('{0} commit(s) ahead, {1} behind', state.ahead, state.behind),
          releaseReminder: state.releaseReminder,
          pendingFinish: state.pendingFinish
            ? { branch: state.pendingFinish.fullName, text: t('Finishing "{0}" stopped on a conflict. Resolve the conflicts, run git add, then resume.', state.pendingFinish.fullName) }
            : null,
        },
        commit: preview ? {
          header: t('Changes ({0})', preview.changes.length),
          changes: preview.changes.map(c => ({
            status: { added: 'A', modified: 'M', deleted: 'D', renamed: 'R', conflicted: 'U' }[c.kind],
            path: c.path,
            title: c.oldPath ? `${c.oldPath} → ${c.path}` : c.path,
          })),
          modeText: preview.mode === 'stagedOnly' ? t('Only the staged files will be committed.') : t('All changes will be committed (new files included).'),
          suggestedMessage: preview.suggestedMessage,
          blocked: preview.blockedMessage,
        } : undefined,
        start: [
          { type: 'feature', label: t('Feature'), prefix: naming.featurePrefix, suggestion: '' },
          { type: 'release', label: t('Release'), prefix: naming.releasePrefix, suggestion: release },
          { type: 'hotfix', label: t('Hotfix'), prefix: naming.hotfixPrefix, suggestion: hotfix },
        ],
        groups: (['feature', 'release', 'hotfix'] as const).map(type => ({
          label: type === 'feature' ? t('Features') : type === 'release' ? t('Releases') : t('Hotfixes'),
          branches: state.flowBranches.filter(b => b.type === type).map(b => ({
            fullName: b.fullName,
            name: b.name,
            badge: [
              state.currentBranch === b.fullName ? t('current') : null,
              state.mergedFlowBranches.includes(b.fullName) ? t('already merged') : null,
            ].filter(Boolean).join('  ·  '),
          })),
        })),
        pullRequests: prs.map(g => ({
          source: g.source,
          items: g.pullRequests.map(p => ({
            label: `#${p.id} → ${p.target}`,
            state: p.state === 'open' ? t('open') : p.state === 'merged' ? t('merged') : p.state === 'closed' ? t('closed without merge') : t('state not checked'),
            url: p.webUrl,
          })),
          tagLabel: t('Create tag {0}', g.version ?? ''),
          canTag: g.canTag,
          canComplete: g.canComplete,
        })),
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.output.error(message);
      return { ...empty('error', repository ? path.basename(repository) : ''), status: message };
    }
  }

  private async update(): Promise<void> {
    const model = await this.buildModel();
    this.lastModel = model;
    await vscode.commands.executeCommand('setContext', 'gitflow.state', model.kind);
    void this.view?.webview.postMessage({ type: 'model', model });
  }

  private async handle(message: PanelMessage): Promise<void> {
    switch (message.type) {
      case 'refresh':
        this.refresher.refresh();
        return;
      case 'refreshPullRequests':
        this.refreshPullRequests = true;
        this.refresher.refresh();
        return;
      case 'showLog':
        this.output.show(true);
        return;
      case 'init':
        await this.flow.init();
        return;
      case 'start':
        if ((await this.flow.start(message.branchType, message.name.trim() || undefined))?.success) {
          void this.view?.webview.postMessage({ type: 'started' });
        }

        return;
      case 'finish':
        await this.flow.finish(message.branch);
        return;
      case 'publish':
        await this.flow.publish(message.branch);
        return;
      case 'tag':
        await this.flow.tagPullRequest(message.branch);
        return;
      case 'complete':
        await this.flow.completePullRequests(message.branch);
        return;
      case 'commit':
        if ((await this.flow.commit(message.message))?.success) {
          void this.view?.webview.postMessage({ type: 'committed' });
        }

        return;
      case 'openUrl':
        if (/^https:\/\//i.test(message.url)) {
          await vscode.env.openExternal(vscode.Uri.parse(message.url));
        }

        return;
    }
  }

  private html(webview: vscode.Webview, media: vscode.Uri): string {
    const nonce = crypto.randomBytes(16).toString('base64');
    const css = webview.asWebviewUri(vscode.Uri.joinPath(media, 'panel.css'));
    const js = webview.asWebviewUri(vscode.Uri.joinPath(media, 'panel.js'));
    return `<!DOCTYPE html>
<html lang="${vscode.env.language}">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link rel="stylesheet" href="${css}">
  <title>GitFlow</title>
</head>
<body>
  <main id="root" aria-live="polite"></main>
  <script nonce="${nonce}" src="${js}"></script>
</body>
</html>`;
  }
}

function typeLabel(type: BranchType | undefined): string {
  return type === 'feature' ? t('Feature') : type === 'release' ? t('Release') : type === 'hotfix' ? t('Hotfix') : t('— (not a feature, release or hotfix branch)');
}

/** Fixed texts of the panel, translated here (the webview has no access to the translations). */
function panelLabels(): Record<string, string> {
  return {
    refresh: t('Refresh'),
    showLog: t('Log'),
    noRepository: t('No Git repository found for the open folder or file.'),
    notInitialized: t('GitFlow is not initialized in this repository.'),
    init: t('Initialize GitFlow…'),
    loading: t('Loading GitFlow…'),
    error: t('GitFlow could not start.'),
    branch: t('Branch'),
    type: t('Type'),
    state: t('State'),
    sync: t('Remote'),
    resume: t('Resume finish…'),
    commitSection: t('Commit'),
    noChanges: t('No changes.'),
    regenerate: t('Regenerate message'),
    commit: t('Commit'),
    commitPlaceholder: t('Commit message'),
    pullRequestsSection: t('Pull requests'),
    refreshPullRequests: t('Refresh PRs'),
    open: t('Open'),
    complete: t('Complete branch'),
    startSection: t('Start a branch'),
    start: t('Start'),
    namePlaceholder: t('name or version'),
    branchesSection: t('Branches in progress'),
    noBranches: t('No branch in progress.'),
    push: t('Push'),
    finish: t('Finish…'),
  };
}
