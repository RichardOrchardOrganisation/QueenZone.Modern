import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { execFileSync, spawn } from 'node:child_process';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const bootstrapPath = new URL('../.github/actions/bitwarden-retry/bootstrap/index.cjs', import.meta.url);
const {
  loadPin, getTargetTriple, binaryFileName, sha256File, findSmActionDir,
  cachePath, stagePath, assetUrl, bootstrap,
} = require(bootstrapPath.pathname);

const ACTION_SHA = '1238aae8fc64b212641190a9227c8a734ab1a793';
const payload = Buffer.from('queenzone-sm-action-bootstrap-fixture');
const sha256 = createHash('sha256').update(payload).digest('hex');
const pin = {
  actionSha: ACTION_SHA,
  version: '3.0.1',
  downloadBase: 'http://127.0.0.1/unused',
  binaries: {
    'aarch64-apple-darwin': { asset: 'sm-action-aarch64-apple-darwin', sha256 },
  },
};

function layout() {
  const root = mkdtempSync(join(tmpdir(), 'qz-sm-action-'));
  const workspaceRoot = join(root, 'work', 'QueenZone.Modern');
  const githubWorkspace = join(workspaceRoot, 'QueenZone.Modern');
  const actionDir = join(root, 'work', '_actions', 'bitwarden', 'sm-action', ACTION_SHA);
  const cacheDir = join(root, 'hostedtoolcache');
  mkdirSync(githubWorkspace, { recursive: true });
  mkdirSync(actionDir, { recursive: true });
  mkdirSync(cacheDir, { recursive: true });
  writeFileSync(join(actionDir, 'index.js'), 'module.exports = {};\n');
  return {
    root, actionDir, cacheDir, githubWorkspace, workspaceRoot,
    env: {
      GITHUB_WORKSPACE: githubWorkspace,
      RUNNER_WORKSPACE: workspaceRoot,
      RUNNER_TOOL_CACHE: cacheDir,
      SM_ACTION_TRIPLE: 'aarch64-apple-darwin',
      SM_ACTION_RETRY_DELAYS_MS: '0,0,0',
      SM_ACTION_DOWNLOAD_TIMEOUT_MS: '2000',
    },
  };
}

function listen(handler) {
  return new Promise((resolve) => {
    const server = createServer(handler);
    server.listen(0, '127.0.0.1', () => {
      resolve({ server, base: `http://127.0.0.1:${server.address().port}` });
    });
  });
}

function closeServer(server) {
  return new Promise((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
}

function runBootstrapCli(env) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [bootstrapPath.pathname], {
      env: { ...process.env, ...env },
    });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stderr.on('data', (chunk) => { stderr += chunk; });
    child.on('close', (status) => resolve({ status, stdout, stderr }));
  });
}

test('pinned checksums match the official v3.0.1 GitHub Release assets', () => {
  const committed = loadPin();
  assert.equal(committed.actionSha, ACTION_SHA);
  assert.equal(committed.version, '3.0.1');
  assert.equal(committed.binaries['aarch64-apple-darwin'].sha256, 'a426480977db65e7ae0d50c5e0ba70508ff15bb1fc803a9b9048e5ccda8d1b73');
  assert.equal(committed.binaries['x86_64-pc-windows-msvc'].asset, 'sm-action-x86_64-pc-windows-msvc.exe');
  assert.equal(getTargetTriple({ SM_ACTION_PLATFORM: 'darwin', SM_ACTION_ARCH: 'arm64' }), 'aarch64-apple-darwin');
  assert.equal(binaryFileName('x86_64-pc-windows-msvc'), 'sm-action.exe');
  assert.equal(assetUrl(committed.downloadBase, '3.0.1', 'sm-action-aarch64-apple-darwin'),
    'https://github.com/bitwarden/sm-action/releases/download/v3.0.1/sm-action-aarch64-apple-darwin');
});

test('bootstrap source never execs rustup, cargo, or a generic child process', () => {
  const source = readFileSync(bootstrapPath, 'utf8');
  assert.doesNotMatch(source, /child_process/);
  assert.doesNotMatch(source, /execSync|execFileSync|spawnSync|spawn\(/);
  assert.doesNotMatch(source, /cargo build|rustup target|Building from source/);
  assert.match(source, /Refusing source-build\/rustup fallback/);
});

test('finds the already-downloaded sm-action checkout without using rustup', () => {
  const fixture = layout();
  assert.equal(findSmActionDir(fixture.env, ACTION_SHA), fixture.actionDir);
  assert.equal(stagePath(fixture.actionDir, 'aarch64-apple-darwin', 'sm-action'),
    join(fixture.actionDir, 'target', 'aarch64-apple-darwin', 'release', 'sm-action'));
});

test('simulated GitHub Releases 503 recovers from runner cache without rustup within 5 minutes', async () => {
  const started = Date.now();
  const fixture = layout();
  const cached = cachePath(fixture.env, pin.version, 'aarch64-apple-darwin', 'sm-action');
  mkdirSync(join(cached, '..'), { recursive: true });
  writeFileSync(cached, payload);
  const requests = [];
  const { server, base } = await listen((_req, res) => {
    requests.push(true);
    res.writeHead(503, { 'Content-Type': 'text/plain' });
    res.end('Service Unavailable');
  });
  try {
    const result = await bootstrap({
      env: { ...fixture.env, SM_ACTION_DOWNLOAD_BASE: base },
      pin,
      retryDelaysMs: [0, 0, 0],
    });
    const elapsed = Date.now() - started;
    console.log(`cache_after_503 elapsed_ms=${elapsed} source=${result.source} rustup=never downloadAttempts=${result.downloadAttempts}`);
    assert.ok(elapsed < 300000, `elapsed ${elapsed}ms`);
    assert.equal(result.source, 'cache');
    assert.equal(result.downloadAttempts, 0);
    assert.equal(requests.length, 0);
    assert.equal(sha256File(result.dest), sha256);
    assert.equal(existsSync(join(fixture.actionDir, 'target', 'aarch64-apple-darwin', 'release', 'sm-action')), true);
  } finally {
    await closeServer(server);
  }
});

test('simulated GitHub Releases 503 then 200 stages the pinned binary without rustup within 5 minutes', async () => {
  const started = Date.now();
  const fixture = layout();
  let remaining503 = 2;
  const { server, base } = await listen((_req, res) => {
    if (remaining503 > 0) {
      remaining503 -= 1;
      res.writeHead(503, { 'Content-Type': 'text/plain' });
      res.end('Service Unavailable');
      return;
    }
    res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Length': payload.length });
    res.end(payload);
  });
  try {
    const result = await bootstrap({
      env: { ...fixture.env, SM_ACTION_DOWNLOAD_BASE: base },
      pin,
      retryDelaysMs: [0, 0, 0],
    });
    const elapsed = Date.now() - started;
    console.log(`retry_after_503 elapsed_ms=${elapsed} source=${result.source} rustup=never downloadAttempts=${result.downloadAttempts}`);
    assert.ok(elapsed < 300000, `elapsed ${elapsed}ms`);
    assert.equal(result.source, 'download');
    assert.equal(result.downloadAttempts, 3);
    assert.equal(remaining503, 0);
    assert.equal(sha256File(result.dest), sha256);
    assert.equal(sha256File(result.cached), sha256);
  } finally {
    await closeServer(server);
  }
});

test('exhausted 503 fails closed without staging or rustup/source-build', async () => {
  const started = Date.now();
  const fixture = layout();
  let hits = 0;
  const { server, base } = await listen((_req, res) => {
    hits += 1;
    res.writeHead(503, { 'Content-Type': 'text/plain' });
    res.end('Service Unavailable');
  });
  try {
    await assert.rejects(
      () => bootstrap({
        env: { ...fixture.env, SM_ACTION_DOWNLOAD_BASE: base },
        pin,
        retryDelaysMs: [0, 0, 0],
      }),
      /Failed to download: 503/,
    );
    const elapsed = Date.now() - started;
    console.log(`exhausted_503 elapsed_ms=${elapsed} rustup=never source_build=false hits=${hits}`);
    assert.ok(elapsed < 300000, `elapsed ${elapsed}ms`);
    assert.equal(hits, 4);
    assert.equal(existsSync(stagePath(fixture.actionDir, 'aarch64-apple-darwin', 'sm-action')), false);
  } finally {
    await closeServer(server);
  }
});

test('CLI subprocess on simulated 503 cache hit never prints rustup or source-build', async () => {
  const fixture = layout();
  const cached = cachePath(fixture.env, pin.version, 'aarch64-apple-darwin', 'sm-action');
  mkdirSync(join(cached, '..'), { recursive: true });
  writeFileSync(cached, payload);
  const pinPath = join(fixture.root, 'pin.json');
  writeFileSync(pinPath, JSON.stringify(pin));
  const outputPath = join(fixture.root, 'github-output');
  writeFileSync(outputPath, '');
  const started = Date.now();
  const output = execFileSync(process.execPath, [bootstrapPath.pathname], {
    encoding: 'utf8',
    env: {
      ...process.env,
      ...fixture.env,
      SM_ACTION_PIN_PATH: pinPath,
      SM_ACTION_CACHE_DIR: fixture.cacheDir,
      GITHUB_OUTPUT: outputPath,
    },
  });
  const elapsed = Date.now() - started;
  console.log(`cli_cache_hit elapsed_ms=${elapsed}`);
  assert.ok(elapsed < 300000, `elapsed ${elapsed}ms`);
  assert.doesNotMatch(output, /rustup|cargo build|Building from source/);
  assert.match(output, /from runner tool cache/);
  assert.equal(readFileSync(outputPath, 'utf8'), 'source=cache\n');
});

test('CLI subprocess exhausted 503 fails closed and names the rustup refusal', async () => {
  const fixture = layout();
  const { server, base } = await listen((_req, res) => {
    res.writeHead(503, { 'Content-Type': 'text/plain' });
    res.end('Service Unavailable');
  });
  let result;
  try {
    result = await runBootstrapCli({
      ...fixture.env,
      SM_ACTION_DOWNLOAD_BASE: base,
    });
  } finally {
    await closeServer(server);
  }
  assert.equal(result.status, 1);
  assert.match(result.stdout + result.stderr, /127\.0\.0\.1/);
  assert.match(result.stderr + result.stdout, /Failed to download: 503/);
  assert.match(result.stderr, /Refusing source-build\/rustup fallback/);
  assert.doesNotMatch(result.stdout + result.stderr, /cargo build|rustup target list/);
  assert.equal(existsSync(stagePath(fixture.actionDir, 'aarch64-apple-darwin', 'sm-action')), false);
});

test('auth-style 404 fails immediately without retries, rustup, or secret output', async () => {
  const fixture = layout();
  let hits = 0;
  const { server, base } = await listen((_req, res) => {
    hits += 1;
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('not found');
  });
  try {
    await assert.rejects(
      () => bootstrap({
        env: { ...fixture.env, SM_ACTION_DOWNLOAD_BASE: base },
        pin,
        retryDelaysMs: [0, 0, 0],
      }),
      /Failed to download: 404/,
    );
    assert.equal(hits, 1);
  } finally {
    await closeServer(server);
  }
});
