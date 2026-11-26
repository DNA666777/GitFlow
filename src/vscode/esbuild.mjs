// Bundles the extension into dist/extension.js (vscode is provided by the editor).
import { build } from 'esbuild';

await build({
  entryPoints: ['src/extension.ts'],
  bundle: true,
  outfile: 'dist/extension.js',
  external: ['vscode'],
  format: 'cjs',
  platform: 'node',
  target: 'node20',
  sourcemap: true,
  minify: process.argv.includes('--minify'),
  logLevel: 'warning',
});
