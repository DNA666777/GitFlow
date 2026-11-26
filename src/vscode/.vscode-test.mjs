// Extension tests in a real VS Code (downloaded by @vscode/test-electron), on a temporary git repository.
import { defineConfig } from '@vscode/test-cli';
import { execFileSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'gitflow-vscode-tests-'));
const workspace = path.join(root, 'repo');
fs.mkdirSync(workspace);
const git = (...args) => execFileSync('git', args, { cwd: workspace, stdio: 'ignore' });
git('init', '-b', 'master');
git('config', 'user.name', 'GitFlow Tests');
git('config', 'user.email', 'tests@example.invalid');
git('config', 'commit.gpgsign', 'false');
fs.writeFileSync(path.join(workspace, 'README.md'), 'initial\n');
git('add', 'README.md');
git('commit', '-m', 'Initial commit');

// User settings of the test instance: run the server with the dotnet on the PATH (no runtime download).
const userData = path.join(root, 'user-data');
fs.mkdirSync(path.join(userData, 'User'), { recursive: true });
fs.writeFileSync(path.join(userData, 'User', 'settings.json'), JSON.stringify({ 'gitflow.dotnetPath': 'dotnet' }));

export default defineConfig({
  files: 'out/test/**/*.test.js',
  workspaceFolder: workspace,
  installExtensions: ['ms-dotnettools.vscode-dotnet-runtime'],
  launchArgs: ['--user-data-dir', userData, '--disable-workspace-trust'],
  mocha: { timeout: 60000 },
});
