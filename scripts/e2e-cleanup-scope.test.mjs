import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const workspace = '/tmp/qz runner.1';
const app = `${workspace}/e2e-app/QueenZone.Web.dll`;
const commands = [
  [`dotnet ${app}`, true],
  [`/opt/sdk/dotnet "${app}"`, true],
  ['dotnet test tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj', false],
  ['dotnet vstest /tmp/QueenZone.Web.E2E.dll', false],
  ['dotnet /tmp/another-checkout/e2e-app/QueenZone.Web.dll', false],
  [`dotnet ${app}.backup`, false],
];

function verifyPattern(pattern) {
  for (const [command, matches] of commands) {
    const result = spawnSync('grep', ['-Eq', pattern], { input: `${command}\n`, encoding: 'utf8' });
    assert.equal(result.error, undefined);
    assert.equal(result.status, matches ? 0 : 1, command);
  }
}

for (const workflow of ['ci.yml', 'nightly-legacy-checks.yml']) {
  test(`${workflow} cleanup only matches its published app`, () => {
    const source = readFileSync(new URL(`../.github/workflows/${workflow}`, import.meta.url), 'utf8');
    const expression = /qz_app_pattern=\$\(python3 -c '([^']+)'\)/.exec(source)?.[1];
    assert.ok(expression, 'Cleanup must construct a scoped pattern before checkout.');
    const result = spawnSync('python3', ['-c', expression], {
      encoding: 'utf8', env: { ...process.env, GITHUB_WORKSPACE: workspace },
    });
    assert.equal(result.status, 0, result.stderr);
    verifyPattern(result.stdout.trim());
    assert.equal(source.includes('pkill -f QueenZone.Web'), false);
  });
}

test('Run-E2E cleanup and stop polling use the same scoped app pattern', () => {
  const source = readFileSync(new URL('./Run-E2E.ps1', import.meta.url), 'utf8');
  const expressions = source.split(/\r?\n/).filter((line) => /^\$macApp(?:Path|Pattern) =/.test(line));
  assert.equal(expressions.length, 2);
  const result = spawnSync('pwsh', ['-NoProfile', '-Command',
    `$repoRoot = '${workspace}'; ${expressions.join('; ')}; Write-Output $macAppPattern`], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  verifyPattern(result.stdout.trim());
  assert.match(source, /pkill -f \$macAppPattern/);
  assert.match(source, /pgrep -f \$macAppPattern/);
  assert.equal(source.includes('pkill -f QueenZone.Web'), false);
});
