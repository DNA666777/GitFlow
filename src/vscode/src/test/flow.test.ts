import * as assert from 'node:assert';
import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { GitFlowApi } from '../extension';
import type { Option, Ui } from '../ui';

/** Scripted answers instead of dialogs; records everything shown. */
class ScriptedUi implements Ui {
  confirms: boolean[] = [];
  inputs: ((prompt: string, value: string) => string | undefined)[] = [];
  picks: ((options: Option[]) => string[] | undefined)[] = [];
  readonly shown: { kind: 'info' | 'error' | 'confirm' | 'input'; text: string; detail?: string }[] = [];

  async input(prompt: string, value: string): Promise<string | undefined> {
    this.shown.push({ kind: 'input', text: prompt });
    const answer = this.inputs.shift();
    assert.ok(answer, `unexpected input: ${prompt}`);
    return answer(prompt, value);
  }

  async confirm(message: string, detail: string): Promise<boolean> {
    this.shown.push({ kind: 'confirm', text: message, detail });
    const answer = this.confirms.shift();
    assert.ok(answer !== undefined, `unexpected confirmation: ${message}`);
    return answer;
  }

  async pickMany(_title: string, options: Option[]): Promise<string[] | undefined> {
    const answer = this.picks.shift();
    assert.ok(answer, 'unexpected options');
    return answer(options);
  }

  async info(message: string, detail?: string): Promise<void> {
    this.shown.push({ kind: 'info', text: message, detail });
  }

  async error(message: string, detail?: string): Promise<void> {
    this.shown.push({ kind: 'error', text: message, detail });
  }

  progress<T>(_title: string, task: () => Promise<T>): Promise<T> {
    return task();
  }

  last(): { kind: string; text: string; detail?: string } {
    return this.shown[this.shown.length - 1];
  }
}

async function setup(): Promise<{ gitflow: GitFlowApi; ui: ScriptedUi; workspace: string; git: (...args: string[]) => string }> {
  const gitflow = await vscode.extensions.getExtension<GitFlowApi>('DNA666777.gitflow-vscode')!.activate();
  const ui = new ScriptedUi();
  gitflow.flow.ui = ui;
  const workspace = vscode.workspace.workspaceFolders![0].uri.fsPath;
  const git = (...args: string[]) => execFileSync('git', args, { cwd: workspace, encoding: 'utf8' }).trim();
  return { gitflow, ui, workspace, git };
}

suite('GitFlow commands (local merge)', () => {
  test('init creates develop after confirmation', async () => {
    const { gitflow, ui, git } = await setup();
    ui.confirms = [true];

    const result = await gitflow.flow.init();

    assert.ok(result?.success, result?.message);
    assert.match(ui.shown[0].detail ?? '', /Production: master/);
    assert.match(git('branch', '--list', 'develop'), /develop/);
    assert.strictEqual(ui.last().kind, 'info');
  });

  test('init on an initialized repository only says so', async () => {
    const { gitflow, ui } = await setup();

    const result = await gitflow.flow.init();

    assert.strictEqual(result, undefined);
    assert.match(ui.last().text, /already initialized/);
  });

  test('feature: start, commit, finish', async () => {
    const { gitflow, ui, workspace, git } = await setup();
    ui.inputs = [() => 'login'];

    const started = await gitflow.flow.start('feature');
    fs.writeFileSync(path.join(workspace, 'login.txt'), 'login\n');
    git('add', 'login.txt');
    git('commit', '-m', 'Add login');
    ui.confirms = [true];
    const finished = await gitflow.flow.finish();

    assert.ok(started?.success, started?.message);
    assert.strictEqual(started.branchName, 'feature/login');
    assert.ok(finished?.success, finished?.message);
    assert.strictEqual(finished.branchName, 'develop');
    assert.match(ui.shown.find(s => s.kind === 'confirm')?.detail ?? '', /merge --no-ff into develop/);
    assert.strictEqual(git('branch', '--list', 'feature/login'), '');
  });

  test('release: suggested version, tag message, tag created', async () => {
    const { gitflow, ui, git } = await setup();
    ui.inputs = [(_p, suggested) => suggested];

    const started = await gitflow.flow.start('release');
    ui.confirms = [true];
    ui.inputs = [(_p, message) => message + ' (VS Code)'];
    const finished = await gitflow.flow.finish();

    assert.ok(started?.success, started?.message);
    const version = started.branchName!.replace('release/', '');
    assert.ok(finished?.success, finished?.message);
    assert.strictEqual(finished.tagName, version);
    assert.strictEqual(git('tag', '-n1', '--list', version).replace(/\s+/g, ' '), `${version} Release ${version} (VS Code)`);
  });

  test('cancelling the confirmation does nothing', async () => {
    const { gitflow, ui, git } = await setup();
    ui.inputs = [() => 'cancelled'];
    await gitflow.flow.start('feature');
    ui.confirms = [false];

    const result = await gitflow.flow.finish();

    assert.strictEqual(result, undefined);
    assert.match(git('branch', '--list', 'feature/cancelled'), /feature\/cancelled/);
    git('switch', 'develop');
    git('branch', '-D', 'feature/cancelled');
  });

  test('finish outside a work branch shows the reason from the server', async () => {
    const { gitflow, ui } = await setup();

    const result = await gitflow.flow.finish();

    assert.strictEqual(result, undefined);
    assert.strictEqual(ui.last().kind, 'error');
    assert.match(ui.last().text, /is not a feature, release or hotfix branch/);
  });

  test('push without origin fails with a message', async () => {
    const { gitflow, ui } = await setup();

    const result = await gitflow.flow.publish();

    assert.strictEqual(result?.success, false);
    assert.strictEqual(ui.last().kind, 'error');
  });
});

suite('Translations', () => {
  test('every text of the extension has a French translation', () => {
    const root = vscode.extensions.getExtension('DNA666777.gitflow-vscode')!.extensionPath;
    const french = JSON.parse(fs.readFileSync(path.join(root, 'l10n', 'bundle.l10n.fr.json'), 'utf8')) as Record<string, string>;
    const sources = fs.readdirSync(path.join(root, 'src')).filter(f => f.endsWith('.ts'));
    const missing: string[] = [];
    for (const file of sources) {
      const text = fs.readFileSync(path.join(root, 'src', file), 'utf8');
      for (const match of text.matchAll(/\bt\('((?:[^'\\]|\\.)*)'|l10n\.t\('((?:[^'\\]|\\.)*)'/g)) {
        const key = (match[1] ?? match[2]).replace(/\\'/g, "'");
        if (!(key in french)) {
          missing.push(`${file}: ${key}`);
        }
      }
    }

    assert.deepStrictEqual(missing, []);

    for (const nls of ['package.nls.json', 'package.nls.fr.json']) {
      assert.ok(fs.existsSync(path.join(root, nls)), nls);
    }

    const english = Object.keys(JSON.parse(fs.readFileSync(path.join(root, 'package.nls.json'), 'utf8')));
    const frenchNls = Object.keys(JSON.parse(fs.readFileSync(path.join(root, 'package.nls.fr.json'), 'utf8')));
    assert.deepStrictEqual(frenchNls.sort(), english.sort());
  });
});
