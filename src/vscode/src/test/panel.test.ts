import * as assert from 'node:assert';
import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { GitFlowApi } from '../extension';
import type { RepoState } from '../protocol';
import type { Ui } from '../ui';

const silentUi: Ui = {
  input: async () => undefined,
  confirm: async () => false,
  pickMany: async () => undefined,
  info: async () => undefined,
  error: async () => undefined,
  progress: (_title, task) => task(),
};

async function setup(): Promise<{ gitflow: GitFlowApi; workspace: string; git: (...args: string[]) => string }> {
  const gitflow = await vscode.extensions.getExtension<GitFlowApi>('DNA666777.gitflow-vscode')!.activate();
  gitflow.flow.ui = silentUi;
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
  return { gitflow, workspace, git };
}

suite('GitFlow panel (same sections as the Visual Studio window)', () => {
  test('state, start types with the suggested versions, branches grouped by type', async () => {
    const { gitflow, git } = await setup();
    git('branch', 'feature/panel-a', 'develop');
    try {
      const model = await gitflow.panel.buildModel();

      assert.strictEqual(model.kind, 'ready');
      assert.strictEqual(model.state?.branch, 'develop');
      assert.strictEqual(model.state?.workingTree, 'Clean');
      assert.deepStrictEqual(model.start.map(s => s.type), ['feature', 'release', 'hotfix']);
      assert.strictEqual(model.start[0].prefix, 'feature/');
      assert.match(model.start[1].suggestion, /^\d+\.\d+\.\d+$/, 'release version suggested');
      assert.deepStrictEqual(model.groups.map(g => g.label), ['Features', 'Releases', 'Hotfixes'], 'the three groups, like in Visual Studio');
      assert.deepStrictEqual(model.groups[0].branches.map(b => b.name), ['panel-a']);
      assert.deepStrictEqual(model.pullRequests, []);
    } finally {
      git('branch', '-D', 'feature/panel-a');
    }
  });

  test('commit section: changes, generated message, then commit', async () => {
    const { gitflow, workspace, git } = await setup();
    await gitflow.flow.start('feature', 'panel-commit');
    fs.writeFileSync(path.join(workspace, 'panel.txt'), 'panel\n');
    try {
      const before = await gitflow.panel.buildModel();
      const result = await gitflow.flow.commit(before.commit!.suggestedMessage);
      const after = await gitflow.panel.buildModel();

      assert.deepStrictEqual(before.commit?.changes.map(c => `${c.status} ${c.path}`), ['A panel.txt']);
      assert.match(before.commit!.suggestedMessage, /panel\.txt/);
      assert.strictEqual(before.commit?.blocked, '');
      assert.ok(result?.success, result?.message);
      assert.strictEqual(git('log', '-1', '--format=%s'), before.commit!.suggestedMessage.split(/\r?\n/)[0]);
      assert.deepStrictEqual(after.commit?.changes, []);
    } finally {
      git('switch', 'develop');
      git('branch', '-D', 'feature/panel-commit');
    }
  });

  test('commit on the production branch is shown as blocked', async () => {
    const { gitflow, workspace, git } = await setup();
    git('switch', 'master');
    fs.writeFileSync(path.join(workspace, 'README.md'), 'changed on master\n');
    try {
      const model = await gitflow.panel.buildModel();

      assert.match(model.commit!.blocked, /master/);
    } finally {
      git('checkout', '--', 'README.md');
      git('switch', 'develop');
    }
  });

  test('a feature just started shows "no commit yet", not "already merged"', async () => {
    const { gitflow, git } = await setup();
    git('branch', 'feature/just-started', 'develop');
    try {
      const model = await gitflow.panel.buildModel();
      const branch = model.groups[0].branches.find(b => b.name === 'just-started')!;

      assert.strictEqual(branch.badge, 'no commit yet');
    } finally {
      git('branch', '-D', 'feature/just-started');
    }
  });

  test('without work branch, only the sentence is shown (no empty groups)', async () => {
    const { gitflow } = await setup();

    const model = await gitflow.panel.buildModel();

    assert.ok(model.groups.every(g => g.branches.length === 0));
    assert.strictEqual(model.labels.noBranches, 'No branch in progress.');
  });

  test('"GitFlow Settings" opens the settings screen in an editor tab', async () => {
    await setup();

    await vscode.commands.executeCommand('gitflow.settings');
    let tab: vscode.Tab | undefined;
    for (let i = 0; i < 50 && !tab; i++) {
      tab = vscode.window.tabGroups.all.flatMap(g => g.tabs).find(x => x.label === 'GitFlow Settings');
      await new Promise(resolve => setTimeout(resolve, 100));
    }

    assert.ok(tab, 'settings tab opened');
    await vscode.window.tabGroups.close(tab);
  });

  test('the panel opens in the Source Control view and receives its model', async () => {
    const { gitflow } = await setup();

    await vscode.commands.executeCommand('gitflow.branches.focus');
    for (let i = 0; i < 100 && gitflow.panel.model?.kind !== 'ready'; i++) {
      await new Promise(resolve => setTimeout(resolve, 100));
    }

    assert.strictEqual(gitflow.panel.model?.kind, 'ready');
  });
});
