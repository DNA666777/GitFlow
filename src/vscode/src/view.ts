import * as vscode from 'vscode';
import { Branch, BranchType, RepoState } from './protocol';
import { GitFlowServer } from './server';

const t = vscode.l10n.t;

/** Quiet time before an automatic refresh. */
export const REFRESH_DELAY_MS = 1000;

/** What the "gitflow.state" context key says (drives the welcome texts of the view). */
export type ViewState = 'loading' | 'noRepository' | 'notInitialized' | 'ready' | 'error';

export type Node =
  | { kind: 'state'; state: RepoState }
  | { kind: 'reminder'; text: string }
  | { kind: 'resume'; branch: Branch }
  | { kind: 'group'; type: BranchType; branches: Branch[]; state: RepoState }
  | { kind: 'branch'; branch: Branch; state: RepoState }
  | { kind: 'empty' };

/** Argument given to Finish / Push when they are run from a branch of the view. */
export function branchOf(argument: unknown): string | undefined {
  if (typeof argument === 'string') {
    return argument;
  }

  const node = argument as Partial<Extract<Node, { kind: 'branch' | 'resume' }>> | undefined;
  return node?.branch?.fullName;
}

/**
 * The "GitFlow" view of the Source Control panel: current branch and state of the repository,
 * work branches in progress grouped by type, with Finish / Push on each branch.
 */
export class GitFlowView implements vscode.TreeDataProvider<Node>, vscode.Disposable {
  private readonly changed = new vscode.EventEmitter<Node | undefined>();
  private refreshTimer: NodeJS.Timeout | undefined;
  private visible = true;
  private stale = false;

  readonly onDidChangeTreeData = this.changed.event;

  /** Last known state of the view (also exposed as the "gitflow.state" context key). */
  viewState: ViewState = 'loading';

  constructor(
    private readonly server: GitFlowServer,
    private readonly findRepository: () => Promise<string | undefined>,
    private readonly output: vscode.LogOutputChannel,
  ) {}

  /** Follows the visibility of the view: no git command while it is hidden, one refresh when it shows again. */
  attach(view: { visible: boolean; onDidChangeVisibility: vscode.Event<{ visible: boolean }> }): void {
    this.visible = view.visible;
    view.onDidChangeVisibility(e => {
      this.visible = e.visible;
      if (e.visible && this.stale) {
        this.refresh();
      }
    });
  }

  /**
   * Refreshes now (Refresh button), or once after a quiet second to coalesce bursts of events
   * (an operation, the git extension, saves, the window getting the focus back). Hidden view: later.
   */
  refresh(debounce = false): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = undefined;
    }

    if (!this.visible) {
      this.stale = true;
      return;
    }

    const fire = () => {
      this.refreshTimer = undefined;
      this.stale = false;
      this.changed.fire(undefined);
    };
    if (debounce) {
      this.refreshTimer = setTimeout(fire, REFRESH_DELAY_MS);
    } else {
      fire();
    }
  }

  dispose(): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
    }

    this.changed.dispose();
  }

  async getChildren(node?: Node): Promise<Node[]> {
    if (node?.kind === 'group') {
      return node.branches.map(branch => ({ kind: 'branch', branch, state: node.state }));
    }

    if (node) {
      return [];
    }

    const state = await this.loadState();
    if (!state || !state.isInitialized) {
      return []; // the welcome text of the view explains what to do
    }

    const nodes: Node[] = [{ kind: 'state', state }];
    if (state.releaseReminder) {
      nodes.push({ kind: 'reminder', text: state.releaseReminder });
    }

    if (state.pendingFinish) {
      nodes.push({ kind: 'resume', branch: state.pendingFinish });
    }

    const groups = (['feature', 'release', 'hotfix'] as const)
      .map(type => ({ kind: 'group' as const, type, branches: state.flowBranches.filter(b => b.type === type), state }))
      .filter(g => g.branches.length > 0);
    nodes.push(...(groups.length > 0 ? groups : [{ kind: 'empty' as const }]));
    return nodes;
  }

  getTreeItem(node: Node): vscode.TreeItem {
    switch (node.kind) {
      case 'state': {
        const s = node.state;
        const item = new vscode.TreeItem(s.currentBranch ?? t('(detached HEAD)'));
        item.description = [stateText(s), syncText(s)].join(' · ');
        item.iconPath = new vscode.ThemeIcon(s.isDirty || s.isMergeInProgress ? 'git-commit' : 'git-branch');
        item.tooltip = new vscode.MarkdownString([
          `**${s.currentBranch ?? 'HEAD'}** — ${typeLabel(s.currentFlowBranch?.type)}`,
          '',
          stateText(s),
          syncText(s),
          '',
          `\`${s.repositoryPath}\``,
        ].join('\n\n'));
        item.contextValue = 'gitflow.state';
        return item;
      }

      case 'reminder': {
        const item = new vscode.TreeItem(node.text);
        item.iconPath = new vscode.ThemeIcon('info');
        item.tooltip = node.text;
        return item;
      }

      case 'resume': {
        const item = new vscode.TreeItem(t('Resume finish of {0}', node.branch.fullName));
        item.iconPath = new vscode.ThemeIcon('warning');
        item.tooltip = t('Finishing "{0}" stopped on a conflict.', node.branch.fullName)
          + '\n' + t('If the conflicts are resolved and the files added (git add), the finish continues where it stopped.');
        item.command = { command: 'gitflow.finish', title: t('Finish'), arguments: [node.branch.fullName] };
        item.contextValue = 'gitflow.resume';
        return item;
      }

      case 'group': {
        const item = new vscode.TreeItem(`${groupLabel(node.type)} (${node.branches.length})`, vscode.TreeItemCollapsibleState.Expanded);
        item.iconPath = new vscode.ThemeIcon(node.type === 'feature' ? 'symbol-event' : node.type === 'release' ? 'tag' : 'flame');
        item.contextValue = 'gitflow.group';
        item.id = `group:${node.type}`;
        return item;
      }

      case 'branch': {
        const b = node.branch;
        const current = node.state.currentBranch === b.fullName;
        const merged = node.state.mergedFlowBranches.includes(b.fullName);
        const item = new vscode.TreeItem(b.name);
        item.description = [current ? t('current') : null, merged ? t('already merged') : null].filter(Boolean).join(' · ');
        item.iconPath = new vscode.ThemeIcon(current ? 'circle-filled' : 'circle-outline');
        item.tooltip = b.fullName;
        item.contextValue = 'gitflow.branch';
        item.id = `branch:${b.fullName}`;
        return item;
      }

      case 'empty':
        return new vscode.TreeItem(t('No branch in progress.'));
    }
  }

  private async loadState(): Promise<RepoState | null> {
    try {
      const repository = await this.findRepository();
      const state = repository ? await this.server.request<RepoState | null>('getState', { repository }) : null;
      await this.setViewState(!state ? 'noRepository' : state.isInitialized ? 'ready' : 'notInitialized');
      return state;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.output.error(message);
      await this.setViewState('error');
      return null;
    }
  }

  private async setViewState(state: ViewState): Promise<void> {
    this.viewState = state;
    await vscode.commands.executeCommand('setContext', 'gitflow.state', state);
  }
}

function typeLabel(type: BranchType | undefined): string {
  return type === 'feature' ? t('Feature') : type === 'release' ? t('Release') : type === 'hotfix' ? t('Hotfix') : t('not a feature, release or hotfix branch');
}

function groupLabel(type: BranchType): string {
  return type === 'feature' ? t('Features') : type === 'release' ? t('Releases') : t('Hotfixes');
}

function stateText(s: RepoState): string {
  return s.isMergeInProgress ? t('Merge in progress') : s.isDirty ? t('Uncommitted changes') : t('No changes');
}

function syncText(s: RepoState): string {
  if (!s.hasRemote) {
    return t('No origin remote');
  }

  return s.ahead === null || s.behind === null
    ? t('Not pushed yet')
    : t('{0} ahead, {1} behind', s.ahead, s.behind);
}
