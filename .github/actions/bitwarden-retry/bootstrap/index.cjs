const fs = require('node:fs');
const http = require('node:http');
const https = require('node:https');
const path = require('node:path');
const crypto = require('node:crypto');

class BootstrapError extends Error {}
const DEFAULT_PIN_PATH = path.join(__dirname, 'pin.json');
const DEFAULT_RETRY_DELAYS_MS = Object.freeze([5000, 15000, 30000]);
const TRANSIENT_STATUS = new Set([408, 429, 500, 502, 503, 504]);
const REFUSAL = 'Refusing source-build/rustup fallback.';

function loadPin(pinPath = DEFAULT_PIN_PATH) {
  return JSON.parse(fs.readFileSync(pinPath, 'utf8'));
}

function getTargetTriple(env = process.env) {
  if (env.SM_ACTION_TRIPLE) return env.SM_ACTION_TRIPLE;
  const archMap = { x64: 'x86_64', arm64: 'aarch64' };
  const platformMap = { linux: 'unknown-linux-gnu', darwin: 'apple-darwin', win32: 'pc-windows-msvc' };
  const arch = archMap[env.SM_ACTION_ARCH || process.arch];
  const platform = platformMap[env.SM_ACTION_PLATFORM || process.platform];
  if (!arch || !platform) {
    throw new BootstrapError(`Unsupported runner platform ${process.platform}/${process.arch}. ${REFUSAL}`);
  }
  return `${arch}-${platform}`;
}

function binaryFileName(triple) {
  return String(triple).includes('pc-windows-msvc') ? 'sm-action.exe' : 'sm-action';
}

function sha256File(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function isValidBinary(filePath, expectedSha) {
  return fs.existsSync(filePath) && sha256File(filePath) === expectedSha;
}

function parseRetryDelays(env = process.env, fallback = DEFAULT_RETRY_DELAYS_MS) {
  const raw = env.SM_ACTION_RETRY_DELAYS_MS;
  if (!raw) return [...fallback];
  const parsed = String(raw).split(',').map((part) => Number(part.trim()));
  if (!parsed.length || parsed.some((value) => !Number.isFinite(value) || value < 0)) {
    throw new BootstrapError('SM_ACTION_RETRY_DELAYS_MS must be a comma-separated list of non-negative milliseconds.');
  }
  return parsed;
}

function findSmActionDir(env, actionSha) {
  if (env.SM_ACTION_DIR) return env.SM_ACTION_DIR;
  const starts = [env.GITHUB_WORKSPACE, env.RUNNER_WORKSPACE, env.RUNNER_TEMP].filter(Boolean);
  for (const start of starts) {
    let dir = path.resolve(start);
    for (let depth = 0; depth < 6; depth += 1) {
      const candidate = path.join(dir, '_actions', 'bitwarden', 'sm-action', actionSha);
      if (fs.existsSync(path.join(candidate, 'index.js'))) return candidate;
      const parent = path.dirname(dir);
      if (parent === dir) break;
      dir = parent;
    }
  }
  throw new BootstrapError(`Pinned sm-action@${actionSha} checkout was not found under _actions. ${REFUSAL}`);
}

function cachePath(env, version, triple, fileName) {
  const root = env.SM_ACTION_CACHE_DIR || env.RUNNER_TOOL_CACHE;
  if (!root) throw new BootstrapError(`Runner tool cache is unavailable. ${REFUSAL}`);
  return path.join(root, 'sm-action', version, triple, fileName);
}

function stagePath(actionDir, triple, fileName) {
  return path.join(actionDir, 'target', triple, 'release', fileName);
}

function assetUrl(base, version, asset) {
  return `${String(base).replace(/\/$/, '')}/v${version}/${asset}`;
}

function copyAtomic(from, to) {
  fs.mkdirSync(path.dirname(to), { recursive: true });
  const temp = `${to}.${process.pid}.partial`;
  fs.copyFileSync(from, temp);
  fs.renameSync(temp, to);
  if (process.platform !== 'win32') fs.chmodSync(to, 0o700);
}

function timeoutError() {
  const error = new BootstrapError('Download timeout');
  error.statusCode = 408;
  error.code = 'ETIMEDOUT';
  return error;
}

function incompleteDownloadError(reason) {
  const error = new BootstrapError(reason);
  error.statusCode = 408;
  error.code = 'ECONNRESET';
  return error;
}

function wrapDownloadError(error) {
  if (error instanceof BootstrapError) return error;
  const wrapped = new BootstrapError(String(error?.message || error));
  wrapped.statusCode = error?.statusCode;
  wrapped.code = error?.code;
  return wrapped;
}

async function destroyDownload({ request, response, file, dest } = {}) {
  if (response) {
    response.unpipe();
    response.destroy();
  }
  if (request) request.destroy();
  if (file && !file.closed) {
    // createWriteStream opens asynchronously. Wait for close before removing
    // the partial, otherwise a pending open can recreate it after cleanup.
    await new Promise(resolve => {
      file.once('close', resolve);
      file.destroy();
    });
  }
  if (dest) fs.rmSync(dest, { force: true });
}

function downloadToFile(url, dest, options = {}) {
  const timeoutMs = options.timeoutMs ?? 30000;
  const maxBytes = options.maxBytes ?? 20 * 1024 * 1024;
  const redirects = options.redirects ?? 0;
  const startedAt = options.startedAt ?? Date.now();
  const remaining = timeoutMs - (Date.now() - startedAt);
  if (remaining <= 0) return Promise.reject(timeoutError());
  return new Promise((resolve, reject) => {
    let settled = false;
    let request;
    let response;
    let file;
    let timer;
    const succeed = () => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve();
    };
    const fail = (error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      destroyDownload({ request, response, file, dest }).then(
        () => reject(wrapDownloadError(error)),
        cleanupError => reject(wrapDownloadError(cleanupError)),
      );
    };
    timer = setTimeout(() => fail(timeoutError()), remaining);
    const lib = url.startsWith('https:') ? https : http;
    request = lib.get(url, { headers: { 'User-Agent': 'QueenZone.Modern-bitwarden-retry' } }, (incoming) => {
      response = incoming;
      const status = incoming.statusCode || 0;
      if ([301, 302, 303, 307, 308].includes(status) && incoming.headers.location) {
        incoming.resume();
        if (redirects >= 5) return fail(new BootstrapError('Too many redirects while downloading sm-action binary.'));
        settled = true;
        clearTimeout(timer);
        request.destroy();
        return downloadToFile(incoming.headers.location, dest, {
          timeoutMs, maxBytes, redirects: redirects + 1, startedAt,
        }).then(resolve, reject);
      }
      if (status !== 200) {
        incoming.resume();
        const error = new BootstrapError(`Failed to download: ${status} ${incoming.statusMessage || ''}`.trim());
        error.statusCode = status;
        return fail(error);
      }
      try {
        fs.mkdirSync(path.dirname(dest), { recursive: true });
        file = fs.createWriteStream(dest);
        let received = 0;
        const expectedLength = Number(incoming.headers['content-length']);
        const failIncomplete = () => fail(incompleteDownloadError(
          'Download closed before the sm-action binary completed.',
        ));
        incoming.on('data', (chunk) => {
          received += chunk.length;
          if (received > maxBytes) fail(new BootstrapError('sm-action binary download exceeded the size cap.'));
        });
        incoming.on('error', fail);
        incoming.on('aborted', failIncomplete);
        incoming.on('close', () => {
          if (!settled && !incoming.complete) failIncomplete();
        });
        file.on('error', fail);
        file.on('finish', () => {
          if (Number.isFinite(expectedLength) && expectedLength >= 0 && received !== expectedLength) {
            return fail(incompleteDownloadError(
              `Download truncated: received ${received} of ${expectedLength} bytes.`,
            ));
          }
          succeed();
        });
        incoming.pipe(file);
      } catch (error) {
        fail(error);
      }
    });
    request.on('error', fail);
  });
}

function isTransient(error) {
  if (TRANSIENT_STATUS.has(error?.statusCode)) return true;
  const code = error?.code;
  if (code === 'ECONNRESET' || code === 'ETIMEDOUT' || code === 'EAI_AGAIN' || code === 'ENOTFOUND') return true;
  return /timeout|aborted|closed before|truncated|socket hang up/i.test(String(error?.message || ''));
}

async function downloadWithRetry({ url, dest, expectedSha, retryDelaysMs, download, wait, attempt = 1 }) {
  const attempts = retryDelaysMs.length + 1;
  const partial = `${dest}.download`;
  fs.rmSync(partial, { force: true });
  try {
    console.log(`Downloading pinned sm-action binary (attempt ${attempt} of ${attempts}).`);
    await download(url, partial);
    if (!isValidBinary(partial, expectedSha)) {
      fs.rmSync(partial, { force: true });
      throw new BootstrapError('Downloaded sm-action binary failed checksum verification.');
    }
    copyAtomic(partial, dest);
    fs.rmSync(partial, { force: true });
    return attempt;
  } catch (error) {
    fs.rmSync(partial, { force: true });
    const transient = isTransient(error) || /checksum/.test(error?.message || '');
    console.log(`Pinned sm-action binary download failed on attempt ${attempt}: ${error?.message || error}`);
    if (!transient || attempt === attempts) {
      throw error instanceof BootstrapError ? error : new BootstrapError(String(error?.message || error));
    }
    await wait(retryDelaysMs[attempt - 1]);
    return downloadWithRetry({ url, dest, expectedSha, retryDelaysMs, download, wait, attempt: attempt + 1 });
  }
}

async function bootstrap(options = {}) {
  const env = options.env || process.env;
  const pin = options.pin || loadPin(options.pinPath || env.SM_ACTION_PIN_PATH || DEFAULT_PIN_PATH);
  const wait = options.wait || ((ms) => (ms ? new Promise((resolve) => setTimeout(resolve, ms)) : Promise.resolve()));
  const download = options.download || ((url, dest) => downloadToFile(url, dest, {
    timeoutMs: Number(env.SM_ACTION_DOWNLOAD_TIMEOUT_MS || 30000),
  }));
  const retryDelaysMs = options.retryDelaysMs || parseRetryDelays(env);
  const triple = getTargetTriple(env);
  const spec = pin.binaries[triple];
  if (!spec) throw new BootstrapError(`No pinned sm-action binary for ${triple}. ${REFUSAL}`);
  const fileName = binaryFileName(triple);
  const actionDir = findSmActionDir(env, pin.actionSha);
  const dest = stagePath(actionDir, triple, fileName);
  const cached = cachePath(env, pin.version, triple, fileName);
  if (isValidBinary(dest, spec.sha256)) {
    console.log(`Using already staged sm-action ${pin.version} ${triple}.`);
    return { source: 'already-staged', triple, version: pin.version, dest, cached, downloadAttempts: 0 };
  }
  if (isValidBinary(cached, spec.sha256)) {
    copyAtomic(cached, dest);
    if (!isValidBinary(dest, spec.sha256)) throw new BootstrapError(`Cached sm-action binary failed to stage. ${REFUSAL}`);
    console.log(`Staged sm-action ${pin.version} ${triple} from runner tool cache.`);
    return { source: 'cache', triple, version: pin.version, dest, cached, downloadAttempts: 0 };
  }
  const url = assetUrl(env.SM_ACTION_DOWNLOAD_BASE || pin.downloadBase, pin.version, spec.asset);
  console.log(`Fetching pinned sm-action binary from ${new URL(url).host}.`);
  const downloadAttempts = await downloadWithRetry({
    url, dest: cached, expectedSha: spec.sha256, retryDelaysMs, download, wait,
  });
  copyAtomic(cached, dest);
  if (!isValidBinary(dest, spec.sha256)) throw new BootstrapError(`Downloaded sm-action binary failed to stage. ${REFUSAL}`);
  console.log(`Staged sm-action ${pin.version} ${triple} from GitHub Releases after ${downloadAttempts} download attempt(s).`);
  return { source: 'download', triple, version: pin.version, dest, cached, downloadAttempts };
}

async function bootstrapFromEnv(env = process.env) {
  try {
    const result = await bootstrap({ env });
    if (env.GITHUB_OUTPUT) fs.appendFileSync(env.GITHUB_OUTPUT, `source=${result.source}\n`);
    return result;
  } catch (error) {
    const message = error instanceof BootstrapError ? error.message : 'sm-action binary bootstrap failed.';
    console.error(message.includes(REFUSAL) ? message : `${message} ${REFUSAL}`);
    throw error instanceof BootstrapError ? error : new BootstrapError(message);
  }
}

if (require.main === module) {
  bootstrapFromEnv(process.env).catch(() => { process.exitCode = 1; });
}

module.exports = {
  BootstrapError, REFUSAL, DEFAULT_RETRY_DELAYS_MS, loadPin, getTargetTriple, binaryFileName,
  sha256File, isValidBinary, parseRetryDelays, findSmActionDir, cachePath, stagePath, assetUrl,
  copyAtomic, downloadToFile, isTransient, downloadWithRetry, bootstrap, bootstrapFromEnv,
};
