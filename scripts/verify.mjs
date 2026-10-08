#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const args = process.argv.slice(2);
const profile = args[args.indexOf('--profile') + 1];
const baseAt = args.indexOf('--base-ref');
const base = baseAt >= 0 ? args[baseAt + 1] : null;
function run(command, arguments_, options = {}) {
  console.log(`Running ${command} ${arguments_.join(' ')}`);
  const result = spawnSync(command, arguments_, { stdio: 'inherit', ...options });
  if (result.status !== 0) throw new Error(`${command} failed: ${result.error?.message || result.status}`);
}
try {
  if (!args.includes('--profile') || !['dotnet', 'typescript'].includes(profile) || !base) throw new Error('Usage: node scripts/verify.mjs --profile dotnet|typescript --base-ref <commit>');
  const config = JSON.parse(readFileSync('development-standards.json', 'utf8'));
  if (config.version !== 1) throw new Error('Unsupported configuration version.');
  if (profile === 'dotnet') {
    const p = config.dotnet;
    if (!p?.solution || !p.reports || !Number.isFinite(p.globalLine) || !Number.isFinite(p.changedLine)) throw new Error('Invalid .NET profile.');
    run('dotnet', ['restore', p.solution]);
    run('dotnet', ['build', p.solution, '--configuration', 'Release', '--no-restore']);
    run('dotnet', ['format', p.solution, '--verify-no-changes', '--no-restore']);
    run('dotnet', ['test', p.solution, '--configuration', 'Release', '--no-build', '--collect:XPlat Code Coverage', '--settings', 'coverlet.runsettings', '--results-directory', p.reports]);
    run('pwsh', ['-NoProfile', '-File', 'scripts/Test-CoverageGate.ps1', '-Reports', p.reports, '-GlobalLineThreshold', String(p.globalLine), '-ChangedLineThreshold', String(p.changedLine), '-BaseRef', base, '-RequireBaseRef']);
    run('pwsh', ['-NoProfile', '-File', 'scripts/Get-CrapReport.ps1', '-Reports', p.reports]);
  } else {
    const p = config.typescript;
    if (!p?.projectRoot || !p.reports || !p.floors) throw new Error('Invalid TypeScript profile.');
    const cwd = path.resolve(p.projectRoot);
    for (const script of ['typecheck', 'lint', 'test:coverage']) {
      // Invoke npm through Node directly so Windows does not require a shell for npm.cmd.
      const npm = spawnSync(process.platform === 'win32' ? 'where.exe' : 'which', [process.platform === 'win32' ? 'npm.cmd' : 'npm'], { encoding: 'utf8' });
      if (npm.status !== 0) throw new Error('npm is unavailable.');
      const npmPath = npm.stdout.trim().split(/\r?\n/)[0];
      if (process.platform === 'win32') run(process.execPath, [path.join(path.dirname(npmPath), 'node_modules/npm/bin/npm-cli.js'), 'run', script], { cwd });
      else run(npmPath, ['run', script], { cwd });
    }
    run(process.execPath, ['scripts/Test-MobileCoverageGate.mjs', '--reports', path.resolve(p.reports), '--floors', path.resolve(p.floors), '--repo-root', process.cwd(), '--base-ref', base]); // QueenZone keeps its mobile gate in place of Test-TypeScriptCoverageGate.mjs (#2116).
  }
  run(process.execPath, ['scripts/check-suppressions.mjs']);
  run(process.execPath, ['scripts/check-feature-map.mjs']);
} catch (error) { console.error(error.message); process.exitCode = 1; }
