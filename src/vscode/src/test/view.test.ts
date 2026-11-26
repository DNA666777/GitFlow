import * as assert from 'node:assert';
import { execFileSync } from 'node:child_process';
import * as vscode from 'vscode';
import type { GitFlowApi } from '../extension';
import type { RepoState } from '../protocol';
import { branchOf, type Node } from '../view';

async function setup(): Promise<{ gitflow: GitFlowApi; git: (...args: string[]) => string }> {
  const gitflow = await vscode.extensions.getExtension<GitFlowApi>('DNA666777.gitflow-vscode')!.activate();
  const workspace = vscode.workspace.workspaceFolders![0].uri.fsPath;
  const git = (...args: string[]) => execFileSync('git', args, { cwd: workspace, encoding: 'utf8' }).trim();

  // Independent of the order of the suites: initialize the repository if needed.
  const repository = (await gitflow.findRepository())!;
  const state = await gitflow.server.request<RepoState>('getState', { repository });
  if (!state.isInitialized) {
    const settings = await gitflow.server.request<object>('getSettings', { repository, scope: 'local' });
    await gitflow.server.request('init', { repository, settings });
  }

  git('switch', 'develop');
  return { gitflow, git };
}

const label = (item: vscode.TreeItem) => (typeof item.label === 'string' ? item.label : item.label?.label ?? '');

suite('GitFlow view', () => {
  test('shows the state, then the work branches grouped by type', async () => {
    const { gitflow, git } = await setup();
    git('branch', 'feature/view-a', 'develop');
    git('branch', 'feature/view-b', 'develop');
    git('branch', 'hotfix/9.9.9', 'master');
    try {
      const roots = await gitflow.view.getChildren();

      assert.strictEqual(gitflow.view.viewState, 'ready');
      assert.strictEqual(roots[0].kind, 'state');
      const stateItem = gitflow.view.getTreeItem(roots[0]);
      assert.strictEqual(label(stateItem), 'develop');
      assert.match(String(stateItem.description), /No changes/);

      const groups = roots.filter((n): n is Extract<Node, { kind: 'group' }> => n.kind === 'group');
      assert.deepStrictEqual(groups.map(g => g.type), ['feature', 'hotfix'], 'empty groups are not shown');
      assert.strictEqual(label(gitflow.view.getTreeItem(groups[0])), 'Features (2)');

      const features = await gitflow.view.getChildren(groups[0]);
      const items = features.map(f => gitflow.view.getTreeItem(f));
      assert.deepStrictEqual(items.map(label).sort(), ['view-a', 'view-b']);
      assert.ok(items.every(i => i.contextValue === 'gitflow.branch'), 'Finish / Push are offered on each branch');
      assert.strictEqual(branchOf(features[0]), (features[0] as Extract<Node, { kind: 'branch' }>).branch.fullName);
    } finally {
      git('branch', '-D', 'feature/view-a', 'feature/view-b', 'hotfix/9.9.9');
    }
  });

  test('marks the current branch and shows the release reminder', async () => {
    const { gitflow, git } = await setup();
    git('switch', '-c', 'release/9.0.0', 'develop');
    try {
      const roots = await gitflow.view.getChildren();
      const reminder = roots.find(n => n.kind === 'reminder');
      const group = roots.find((n): n is Extract<Node, { kind: 'group' }> => n.kind === 'group' && n.type === 'release')!;
      const [branch] = await gitflow.view.getChildren(group);

      assert.ok(reminder, 'release reminder');
      assert.match(String(gitflow.view.getTreeItem(branch).description), /current/);
    } finally {
      git('switch', 'develop');
      git('branch', '-D', 'release/9.0.0');
    }
  });

  test('without work branch, says so', async () => {
    const { gitflow } = await setup();

    const roots = await gitflow.view.getChildren();

    assert.ok(roots.some(n => n.kind === 'empty'));
    assert.strictEqual(label(gitflow.view.getTreeItem(roots.find(n => n.kind === 'empty')!)), 'No branch in progress.');
  });

  test('the view is contributed to the Source Control panel', async () => {
    await setup();
    const manifest = vscode.extensions.getExtension('DNA666777.gitflow-vscode')!.packageJSON as {
      contributes: { views: Record<string, { id: string }[]> };
    };

    assert.deepStrictEqual(manifest.contributes.views.scm.map(v => v.id), ['gitflow.branches']);
  });
});
