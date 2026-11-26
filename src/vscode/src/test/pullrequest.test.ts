import * as assert from 'node:assert';
import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as http from 'node:http';
import type { AddressInfo } from 'node:net';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import type { GitFlowApi } from '../extension';
import type { RepoState } from '../protocol';
import { ScriptedUi } from './flow.test';

interface FakePullRequest {
  pullRequestId: number;
  status: string;
  title: string;
  description: string;
  sourceRefName: string;
  targetRefName: string;
}

/**
 * A fake Azure DevOps Server (the company case): the REST API of the pull requests, on localhost.
 * git itself pushes to a real bare repository through url.insteadOf.
 */
async function startFakeAzureDevOps(): Promise<{ url: string; requests: { method: string; url: string; auth?: string }[]; prs: FakePullRequest[]; close(): void }> {
  const requests: { method: string; url: string; auth?: string }[] = [];
  const prs: FakePullRequest[] = [];
  const server = http.createServer((req, res) => {
    let body = '';
    req.on('data', chunk => (body += chunk));
    req.on('end', () => {
      requests.push({ method: req.method ?? '', url: req.url ?? '', auth: req.headers.authorization });
      const send = (status: number, value: unknown) => {
        res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
        res.end(JSON.stringify(value));
      };
      const url = new URL(req.url ?? '/', 'http://localhost');
      const byId = /\/pullrequests\/(\d+)$/.exec(url.pathname);
      if (req.method === 'POST' && url.pathname.endsWith('/pullrequests')) {
        const draft = JSON.parse(body) as { sourceRefName: string; targetRefName: string; title: string; description: string };
        const pr = { pullRequestId: prs.length + 1, status: 'active', ...draft };
        prs.push(pr);
        send(201, pr);
      } else if (req.method === 'GET' && byId) {
        const pr = prs.find(p => p.pullRequestId === Number(byId[1]));
        send(pr ? 200 : 404, pr ?? { message: 'not found' });
      } else if (req.method === 'GET' && url.pathname.endsWith('/pullrequests')) {
        const source = url.searchParams.get('searchCriteria.sourceRefName');
        const target = url.searchParams.get('searchCriteria.targetRefName');
        send(200, { value: prs.filter(p => p.sourceRefName === source && p.targetRefName === target) });
      } else {
        send(404, { message: 'unexpected ' + req.method + ' ' + req.url });
      }
    });
  });
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as AddressInfo).port;
  return { url: `http://localhost:${port}/tfs/Coll/Proj/_git/Repo`, requests, prs, close: () => server.close() };
}

suite('Finish by Pull Request (fake Azure DevOps Server)', () => {
  test('mode choice, review, push and pull request, then followed in the panel', async () => {
    const gitflow = await vscode.extensions.getExtension<GitFlowApi>('DNA666777.gitflow-for-vscode')!.activate();
    const workspace = vscode.workspace.workspaceFolders![0].uri.fsPath;
    const git = (...args: string[]) => execFileSync('git', args, { cwd: workspace, encoding: 'utf8' }).trim();
    const repository = (await gitflow.findRepository())!;
    const state = await gitflow.server.request<RepoState>('getState', { repository });
    if (!state.isInitialized) {
      const settings = await gitflow.server.request<object>('getSettings', { repository, scope: 'local' });
      await gitflow.server.request('init', { repository, settings });
    }

    const fake = await startFakeAzureDevOps();
    const bare = fs.mkdtempSync(path.join(os.tmpdir(), 'gitflow-origin-'));
    execFileSync('git', ['init', '--bare', '-q', bare]);
    try {
      git('remote', 'add', 'origin', fake.url);
      git('config', '--local', `url.${bare.replace(/\\/g, '/')}.insteadOf`, fake.url);
      git('config', '--local', 'credential.helper', ''); // no credential helper: the Windows session is used
      git('config', '--local', 'gitflowvs.pr.platform', 'azuredevops');
      git('push', '-q', 'origin', 'master', 'develop');
      git('switch', 'develop');

      const ui = new ScriptedUi();
      gitflow.flow.ui = ui;
      ui.inputs = [() => 'via-pr'];
      await gitflow.flow.start('feature');
      fs.writeFileSync(path.join(workspace, 'via-pr.txt'), 'pull request\n');
      git('add', 'via-pr.txt');
      git('commit', '-q', '-m', 'Add via-pr');

      ui.confirms = [true, false]; // Finish, then do not open the browser
      ui.modes = [options => {
        assert.deepStrictEqual(options.map(o => o.id), ['local', 'pullrequest']);
        return 'pullrequest';
      }];
      ui.reviews = [drafts => {
        assert.deepStrictEqual(drafts.map(d => d.target), ['develop']);
        assert.strictEqual(drafts[0].title, 'Feature: via pr');
        return drafts.map(d => ({ ...d, title: d.title + ' (reviewed)' }));
      }];
      const result = await gitflow.flow.finish();

      assert.ok(result?.success, result?.message);
      assert.deepStrictEqual(result.pullRequests.map(p => p.target), ['develop']);
      assert.strictEqual(fake.prs[0].title, 'Feature: via pr (reviewed)');
      assert.strictEqual(fake.prs[0].sourceRefName, 'refs/heads/feature/via-pr');
      assert.ok(fake.requests.every(r => r.url.includes('api-version=4.1')), 'Azure DevOps Server API');
      assert.ok(fake.requests.every(r => !r.auth), 'no secret sent: Windows authentication');
      assert.match(execFileSync('git', ['branch', '--list', 'feature/via-pr'], { cwd: bare, encoding: 'utf8' }), /via-pr/, 'branch pushed');
      assert.strictEqual(git('rev-parse', 'develop'), git('rev-parse', 'origin/develop'), 'nothing merged locally');

      const model = await gitflow.panel.buildModel();
      const group = model.pullRequests.find(g => g.source === 'feature/via-pr');
      assert.ok(group, 'the pull request is followed in the panel');
      assert.strictEqual(group.canComplete, false);
    } finally {
      fake.close();
      for (const args of [
        ['switch', 'develop'],
        ['branch', '-D', 'feature/via-pr'],
        ['remote', 'remove', 'origin'],
        ['config', '--local', '--remove-section', 'gitflowvs-pr.feature/via-pr'],
        ['config', '--local', '--unset', 'gitflowvs.pr.platform'],
        ['config', '--local', '--unset', 'credential.helper'],
        ['config', '--local', '--remove-section', `url.${bare.replace(/\\/g, '/')}`],
      ]) {
        try {
          git(...args);
        } catch {
          // already clean
        }
      }
    }
  });
});
