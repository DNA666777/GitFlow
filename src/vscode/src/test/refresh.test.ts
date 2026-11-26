import * as assert from 'node:assert';
import * as vscode from 'vscode';
import { isInsideGitFolder } from '../extension';
import { Refresher, REFRESH_DELAY_MS } from '../refresher';

/** A view whose visibility the test controls. */
function fakeView(visible: boolean) {
  const emitter = new vscode.EventEmitter<void>();
  return {
    visible,
    onDidChangeVisibility: emitter.event,
    show(value: boolean) {
      this.visible = value;
      emitter.fire();
    },
  };
}

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

suite('Refresh and log (4b)', () => {
  test('a burst of events gives a single refresh', async () => {
    let refreshes = 0;
    const refresher = new Refresher(() => refreshes++);
    refresher.attach(fakeView(true));

    for (let i = 0; i < 5; i++) {
      refresher.refresh(true);
      await wait(50);
    }

    assert.strictEqual(refreshes, 0, 'nothing before the quiet time');
    await wait(REFRESH_DELAY_MS + 200);
    assert.strictEqual(refreshes, 1);
    refresher.dispose();
  });

  test('the Refresh button is immediate', () => {
    let refreshes = 0;
    const refresher = new Refresher(() => refreshes++);
    refresher.attach(fakeView(true));

    refresher.refresh();

    assert.strictEqual(refreshes, 1);
    refresher.dispose();
  });

  test('a hidden view is refreshed once when it shows again', async () => {
    let refreshes = 0;
    const refresher = new Refresher(() => refreshes++);
    const view = fakeView(false);
    refresher.attach(view);

    refresher.refresh(true);
    refresher.refresh();
    await wait(REFRESH_DELAY_MS + 200);
    assert.strictEqual(refreshes, 0, 'no git command while hidden');

    view.show(true);
    assert.strictEqual(refreshes, 1);
    refresher.dispose();
  });

  test('files inside .git are not used to find the repository', () => {
    assert.ok(isInsideGitFolder('C:\\work\\repo\\.git\\COMMIT_EDITMSG'));
    assert.ok(isInsideGitFolder('/home/me/repo/.git/MERGE_MSG'));
    assert.ok(!isInsideGitFolder('C:\\work\\repo\\src\\app.ts'));
    assert.ok(!isInsideGitFolder('/home/me/repo/.github/workflows/ci.yml'));
  });
});
