import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { spawnPwsh } from './test-helpers/pwsh.mjs';

test('mirror migrations expose compiler failures and never run EF after a failed build', () => {
  const source = readFileSync(new URL('./Run-E2E.ps1', import.meta.url), 'utf8');
  const helper = /function Update-SqlExpressMirrorMigrations \{[\s\S]*?\n\}/.exec(source)?.[0];
  assert.ok(helper);
  for (const failBuild of [false, true]) {
    const result = spawnPwsh(['-Command', `
      $Configuration = 'Release'
      function Invoke-DotNet {
        param([string[]]$Arguments)
        Write-Output ('CAPTURE=' + (ConvertTo-Json -Compress -InputObject @($Arguments)))
        if ($Arguments[0] -eq 'build' -and $${failBuild}) { throw 'offline compiler failure' }
      }
      ${helper}
      Update-SqlExpressMirrorMigrations
    `], { encoding: 'utf8' });
    assert.equal(result.error, undefined);
    const calls = result.stdout.split(/\r?\n/).filter((line) => line.startsWith('CAPTURE='))
      .map((line) => JSON.parse(line.slice(8)));
    assert.deepEqual(calls.map((args) => args[0]), failBuild ? ['tool', 'restore', 'build'] : ['tool', 'restore', 'build', 'ef']);
    assert.equal(calls[2][calls[2].indexOf('--configuration') + 1], 'Release');
    assert.ok(calls[2].includes('--no-restore'));
    if (failBuild) {
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /offline compiler failure/);
    } else {
      assert.equal(result.status, 0, result.stderr);
      assert.ok(calls[3].includes('--no-build'));
      assert.equal(calls[3][calls[3].indexOf('--configuration') + 1], 'Release');
    }
  }
});
