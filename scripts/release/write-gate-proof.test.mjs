import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import {
  NON_MAIN_BUILD_ERROR,
  formatOlderMainNotice,
  isOnMainCompare,
  parseDevBuildStamp,
  resolveDevStampOnMain,
  writeGateProofFromArgs,
} from './write-gate-proof.mjs';

const tip = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const older = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';
const other = 'cccccccccccccccccccccccccccccccccccccccc';
const repo = 'owner/repo';
const token = 'test-token';

function jsonResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    async json() {
      return body;
    },
    async text() {
      return JSON.stringify(body);
    },
  };
}

function githubFetch({ commits = {}, compares = {} } = {}) {
  const calls = [];
  const fetchImpl = async (url) => {
    calls.push(url);
    const parsed = new URL(url);
    const commitMatch = parsed.pathname.match(/\/commits\/([^/]+)$/);
    if (commitMatch) {
      const key = decodeURIComponent(commitMatch[1]).toLowerCase();
      if (Object.prototype.hasOwnProperty.call(commits, key)) {
        const commit = commits[key];
        if (commit === null) {
          return jsonResponse({ message: 'Not Found' }, 404);
        }
        return jsonResponse(commit);
      }
      return jsonResponse({ message: 'Not Found' }, 404);
    }
    const compareMatch = parsed.pathname.match(/\/compare\/(.+)$/);
    if (compareMatch) {
      const key = decodeURIComponent(compareMatch[1]);
      if (Object.prototype.hasOwnProperty.call(compares, key)) {
        return jsonResponse(compares[key]);
      }
      throw new Error(`unexpected compare ${key}`);
    }
    throw new Error(`unexpected GitHub URL ${url}`);
  };
  return { fetchImpl, calls };
}

function githubEnv(extra = {}) {
  return {
    GITHUB_REPOSITORY: repo,
    GITHUB_TOKEN: token,
    ...extra,
  };
}

test('parseDevBuildStamp rejects malformed and empty stamps', () => {
  assert.equal(parseDevBuildStamp(tip.slice(0, 7)), tip.slice(0, 7));
  assert.throws(() => parseDevBuildStamp(''), /missing data-build-version/);
  assert.throws(() => parseDevBuildStamp('not-a-sha'), /Malformed DEV build stamp/);
  assert.throws(() => parseDevBuildStamp('main'), /Malformed DEV build stamp/);
  assert.throws(() => parseDevBuildStamp('abcd'), /Malformed DEV build stamp/);
});

test('isOnMainCompare accepts only identical or ancestor-of-main compares', () => {
  assert.equal(isOnMainCompare({ status: 'identical' }), true);
  assert.equal(isOnMainCompare({ status: 'ahead', behind_by: 0 }), true);
  assert.equal(isOnMainCompare({ status: 'ahead', behind_by: 1 }), false);
  assert.equal(isOnMainCompare({ status: 'diverged', behind_by: 1 }), false);
  assert.equal(isOnMainCompare({ status: 'behind', behind_by: 1 }), false);
  assert.equal(isOnMainCompare(null), false);
});

test('stamp equal to the tip writes proof for that commit', async () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'qz-gate-proof-tip-'));
  const out = path.join(directory, 'gate-proof.json');
  const output = path.join(directory, 'github-output');
  const { fetchImpl, calls } = githubFetch({
    commits: { [tip.slice(0, 7)]: { sha: tip } },
    compares: { [`${tip}...main`]: { status: 'identical', behind_by: 0 } },
  });
  try {
    const written = await writeGateProofFromArgs(
      ['--stamp', tip.slice(0, 7), '--out', out, '--sha', tip],
      githubEnv({ GITHUB_OUTPUT: output }),
      fetchImpl,
    );
    assert.deepEqual(written, { tested_sha: tip, full_suite: true });
    assert.deepEqual(JSON.parse(readFileSync(out, 'utf8')), written);
    assert.equal(readFileSync(output, 'utf8'), 'wrote=true\n');
    assert.ok(calls.some((url) => url.endsWith(`/commits/${tip.slice(0, 7)}`)));
    assert.ok(calls.some((url) => url.endsWith(`/compare/${tip}...main`)));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('older main stamp writes no artifact and ends green', async () => {
  const directory = mkdtempSync(path.join(tmpdir(), 'qz-gate-proof-older-'));
  const out = path.join(directory, 'gate-proof.json');
  const output = path.join(directory, 'github-output');
  const summary = path.join(directory, 'summary.md');
  const logs = [];
  const originalLog = console.log;
  const { fetchImpl } = githubFetch({
    commits: { [older.slice(0, 7)]: { sha: older } },
    compares: { [`${older}...main`]: { status: 'ahead', ahead_by: 1, behind_by: 0 } },
  });
  try {
    console.log = (...args) => logs.push(args.join(' '));
    const result = await writeGateProofFromArgs(
      ['--stamp', older.slice(0, 7), '--out', out, '--sha', tip],
      githubEnv({ GITHUB_OUTPUT: output, GITHUB_STEP_SUMMARY: summary }),
      fetchImpl,
    );
    const notice = formatOlderMainNotice(older, tip);
    assert.deepEqual(result, { skipped: true, servedSha: older, tipSha: tip, notice });
    assert.equal(existsSync(out), false);
    assert.equal(readFileSync(output, 'utf8'), 'wrote=false\n');
    assert.equal(readFileSync(summary, 'utf8'), `${notice}\n`);
    assert.ok(logs.some((line) => line === `::notice::${notice}`));
    assert.match(notice, /DEV is on /);
    assert.match(notice, /tip is /);
  } finally {
    console.log = originalLog;
    rmSync(directory, { recursive: true, force: true });
  }
});

test('stamp not on main fails red', async () => {
  const { fetchImpl } = githubFetch({
    commits: { [other.slice(0, 7)]: { sha: other } },
    compares: { [`${other}...main`]: { status: 'diverged', ahead_by: 3, behind_by: 1 } },
  });
  await assert.rejects(
    () => resolveDevStampOnMain(other.slice(0, 7), {
      tipSha: tip,
      repo,
      token,
      fetchImpl,
    }),
    (error) => {
      assert.match(error.message, new RegExp(NON_MAIN_BUILD_ERROR));
      assert.match(error.message, new RegExp(other));
      return true;
    },
  );
});

test('malformed stamp fails red without calling GitHub', async () => {
  const { fetchImpl, calls } = githubFetch();
  await assert.rejects(
    () => writeGateProofFromArgs(
      ['--stamp', 'not-a-sha', '--out', 'unused.json', '--sha', tip],
      githubEnv(),
      fetchImpl,
    ),
    /Malformed DEV build stamp/,
  );
  assert.deepEqual(calls, []);
});

test('unknown stamp from GitHub is not replaced with the run SHA', async () => {
  const { fetchImpl } = githubFetch({
    commits: { ccccccc: null },
  });
  await assert.rejects(
    () => writeGateProofFromArgs(
      ['--stamp', 'ccccccc', '--out', 'unused.json', '--sha', tip],
      githubEnv(),
      fetchImpl,
    ),
    /Unknown DEV build stamp/,
  );
});
