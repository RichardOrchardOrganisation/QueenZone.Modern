import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('proof archive and packaging regressions', () => {
  const result = spawnSync(process.platform === 'win32' ? 'python' : 'python3', [fileURLToPath(new URL('./test_ui_proof_bundle.py', import.meta.url))], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stdout + result.stderr);
});
