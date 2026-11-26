import * as assert from 'node:assert';
import * as vscode from 'vscode';
import { isInsideGitFolder, type GitFlowApi } from '../extension';
import { GitFlowView, REFRESH_DELAY_MS } from '../view';

/** A view whose visibility the test controls. */
function fakeTreeView(visible: boolean) {
  const emitter = new vscode.EventEmitter<{ visible: boolean }>();
  return {
    visible,
    onDidChangeVisibility: emitter.event,
    show(value: boolean) {
      this.visible = value;
      emitter.fire({ visible: value });
    },
  };
}

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

suite('Refresh and log (4b)', () => {
  async function newView(): Promise<{ view: GitFlowView; refreshes: () => number }> {
    const gitflow = await vscode.extensions.getExtension<GitFlowApi>('DNA666777.gitflow-vscode')!.activate();
    const view = new GitFlowView(gitflow.server, gitflow.findRepository, vscode.window.createOutputChannel('GitFlow test', { log: true }));
    let count = 0;
    view.onDidChangeTreeData(() => count++);
    return { view, refreshes: () => count };
  }

  test('a burst of events gives a single refresh', async () => {
    const { view, refreshes } = await newView();
    view.attach(fakeTreeView(true));

    for (let i = 0; i < 5; i++) {
      view.refresh(true);
      await wait(50);
    }

    assert.strictEqual(refreshes(), 0, 'nothing before the quiet time');
    await wait(REFRESH_DELAY_MS + 200);
    assert.strictEqual(refreshes(), 1);
    view.dispose();
  });

  test('a hidden view is refreshed once when it shows again', async () => {
    const { view, refreshes } = await newView();
    const tree = fakeTreeView(false);
    view.attach(tree);

    view.refresh(true);
    view.refresh();
    await wait(REFRESH_DELAY_MS + 200);
    assert.strictEqual(refreshes(), 0, 'no git command while hidden');

    tree.show(true);
    assert.strictEqual(refreshes(), 1);
    view.dispose();
  });

  test('files inside .git are not used to find the repository', () => {
    assert.ok(isInsideGitFolder('C:\\work\\repo\\.git\\COMMIT_EDITMSG'));
    assert.ok(isInsideGitFolder('/home/me/repo/.git/MERGE_MSG'));
    assert.ok(!isInsideGitFolder('C:\\work\\repo\\src\\app.ts'));
    assert.ok(!isInsideGitFolder('/home/me/repo/.github/workflows/ci.yml'));
  });
});
