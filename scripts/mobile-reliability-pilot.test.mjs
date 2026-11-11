import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

test('pilot preparation uses installed tool paths and rejects relative roots', (t) => {
  const directory = mkdtempSync(join(tmpdir(), 'qz-pilot-tools-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const dotnetRoot = join(directory, 'dotnet-sdk');
  const javaHome = join(directory, 'java-sdk');
  mkdirSync(dotnetRoot);
  mkdirSync(join(javaHome, 'bin'), { recursive: true });
  writeFileSync(join(dotnetRoot, 'dotnet'), '#!/bin/sh\nprintf "dotnet %s\\n" "$@"\n', { mode: 0o755 });
  writeFileSync(join(javaHome, 'bin', 'java'), '#!/bin/sh\nprintf "java %s\\n" "$@"\n', { mode: 0o755 });

  const pilot = fileURLToPath(new URL('./mobile-reliability-pilot.mjs', import.meta.url));
  const run = (root) => spawnSync(process.execPath, [pilot, 'prepare', 'photos-baseline'], {
    encoding: 'utf8',
    env: { RUNNER_OS: 'Linux', FORCE_HOSTED_IOS: 'true', DOTNET_ROOT: root, JAVA_HOME: javaHome, PATH: '' },
  });

  const success = run(dotnetRoot);
  assert.equal(success.status, 0, success.stderr);
  assert.match(success.stdout, /dotnet --version\njava -version/);

  const invalid = run('relative-sdk');
  assert.equal(invalid.status, 1);
  assert.match(invalid.stderr, /DOTNET_ROOT must be an absolute tool installation path/);
});
