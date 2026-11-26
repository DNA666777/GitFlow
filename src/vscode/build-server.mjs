// Publishes GitFlow.Server into server/ with the version of this package (shown in the GitFlow log).
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const { version } = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8'));
execFileSync('dotnet', [
  'publish', '../GitFlow.Server/GitFlow.Server.csproj',
  '-c', 'Release', '-o', 'server', '--nologo', `-p:Version=${version}`,
], { stdio: 'inherit' });
