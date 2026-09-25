#!/usr/bin/env node
/**
 * PR Verification section check (#1799).
 *
 * A UI PR must have ## Verification with a feature-map id and either a proof
 * link or a Not verified: line. Opt out with the no-ui-verification label plus
 * Verification-skip-reason:. Dependabot is exempt.
 */
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { listUiSourcePaths, loadFeatureMap, repoRootFrom, toPosix } from './check-feature-map.mjs';

export const NEEDS_VERIFICATION = 'needs-verification';
export const NO_UI_VERIFICATION = 'no-ui-verification';

const MOBILE_UI = /^src\/QueenZone\.Mobile\/src\/(screens|navigation|ui)\//;
const WEB_UI = /^src\/QueenZone\.Web\/(Pages|Views|wwwroot)\//;

export function isIgnoredVerificationPath(file) {
  const posix = toPosix(file);
  if (posix.startsWith('docs/')) {
    return true;
  }
  if (posix.endsWith('.md')) {
    return true;
  }
  if (/\.(test|spec)\.[cm]?[jt]sx?$/.test(posix)) {
    return true;
  }
  if (/Tests\.cs$/.test(posix)) {
    return true;
  }
  if (posix.startsWith('tests/') || posix.includes('/tests/')) {
    return true;
  }
  return false;
}

export function isUiChangedPath(file, mobileSources) {
  const posix = toPosix(file);
  if (isIgnoredVerificationPath(posix)) {
    return false;
  }
  if (MOBILE_UI.test(posix) || WEB_UI.test(posix)) {
    return true;
  }
  return mobileSources.has(posix);
}

export function extractVerificationSection(body) {
  const text = String(body || '');
  const start = text.match(/^## Verification\b[^\n]*/m);
  if (!start) {
    return null;
  }
  const from = start.index;
  const afterHeading = text.slice(from + start[0].length);
  const nextHeading = afterHeading.search(/\n## /);
  return nextHeading === -1 ? text.slice(from) : text.slice(from, from + start[0].length + nextHeading);
}

export function findFeatureIds(text, ids) {
  const haystack = String(text || '');
  return [...ids].filter((id) => haystack.includes(id)).sort();
}

export function hasProofLink(text) {
  return /https?:\/\//.test(text) || /artifacts\/proof\//.test(text);
}

export function hasNotVerified(text) {
  return /Not verified:/i.test(text);
}

export function verificationSkipReason(body) {
  const match = String(body || '').match(/Verification-skip-reason:\s*(\S.*)$/im);
  return match ? match[1].trim() : '';
}

export function isDependabot(pullRequest) {
  const login = pullRequest?.user?.login || '';
  const ref = pullRequest?.head?.ref || '';
  return login === 'dependabot[bot]' || ref.startsWith('dependabot/');
}

export function evaluatePrVerification({
  body = '',
  files = [],
  labels = [],
  dependabot = false,
  mapIds = new Set(),
  mobileSources = new Set(),
} = {}) {
  if (dependabot) {
    return { ok: true, ui: false, reason: 'Dependabot is exempt.' };
  }

  const uiFiles = files.filter((file) => isUiChangedPath(file, mobileSources));
  const ui = uiFiles.length > 0;
  const labelNames = labels.map((label) => (typeof label === 'string' ? label : label.name));
  const skipLabel = labelNames.includes(NO_UI_VERIFICATION);
  const skipReason = verificationSkipReason(body);

  if (skipLabel) {
    if (!skipReason) {
      return {
        ok: false,
        ui,
        uiFiles,
        error:
          'no-ui-verification is set but the PR body has no Verification-skip-reason: line.',
      };
    }
    return { ok: true, ui, uiFiles, skipped: true, skipReason };
  }

  if (!ui) {
    return { ok: true, ui: false, uiFiles };
  }

  const section = extractVerificationSection(body);
  if (!section) {
    return {
      ok: false,
      ui,
      uiFiles,
      error: 'UI PR is missing a ## Verification section.',
    };
  }

  const ids = findFeatureIds(section, mapIds);
  if (ids.length === 0) {
    return {
      ok: false,
      ui,
      uiFiles,
      error: '## Verification must name at least one feature-map id.',
    };
  }

  if (!hasProofLink(section) && !hasNotVerified(section)) {
    return {
      ok: false,
      ui,
      uiFiles,
      ids,
      error: '## Verification needs a proof link or a Not verified: line.',
    };
  }

  return { ok: true, ui, uiFiles, ids };
}

async function listPullFiles(github, context, pullNumber) {
  const files = [];
  const iterator = github.paginate.iterator(github.rest.pulls.listFiles, {
    owner: context.repo.owner,
    repo: context.repo.repo,
    pull_number: pullNumber,
    per_page: 100,
  });
  for await (const response of iterator) {
    for (const file of response.data) {
      files.push(file.filename);
    }
  }
  return files;
}

async function ensureLabel(github, context, name, color, description) {
  try {
    await github.rest.issues.getLabel({
      owner: context.repo.owner,
      repo: context.repo.repo,
      name,
    });
  } catch (error) {
    if (error.status !== 404) {
      throw error;
    }
    await github.rest.issues.createLabel({
      owner: context.repo.owner,
      repo: context.repo.repo,
      name,
      color,
      description,
    });
  }
}

async function setNeedsVerification(github, context, pullNumber, currentLabels, needed) {
  await ensureLabel(
    github,
    context,
    NEEDS_VERIFICATION,
    'd93f0b',
    'UI PR is missing required verification proof.',
  );
  const has = currentLabels.includes(NEEDS_VERIFICATION);
  if (needed && !has) {
    await github.rest.issues.addLabels({
      owner: context.repo.owner,
      repo: context.repo.repo,
      issue_number: pullNumber,
      labels: [NEEDS_VERIFICATION],
    });
  }
  if (!needed && has) {
    await github.rest.issues.removeLabel({
      owner: context.repo.owner,
      repo: context.repo.repo,
      issue_number: pullNumber,
      name: NEEDS_VERIFICATION,
    });
  }
}

export async function checkPullRequestVerification({ github, context, core, root = repoRootFrom() }) {
  const pullRequest = context.payload.pull_request;
  if (!pullRequest) {
    core.info('No pull request payload; skipping verification check.');
    return { ok: true, skipped: true };
  }

  const map = loadFeatureMap(root);
  const mapIds = new Set(map.entries.map((entry) => entry.id));
  const mobileSources = listUiSourcePaths(map);
  const files = await listPullFiles(github, context, pullRequest.number);
  const result = evaluatePrVerification({
    body: pullRequest.body || '',
    files,
    labels: pullRequest.labels || [],
    dependabot: isDependabot(pullRequest),
    mapIds,
    mobileSources,
  });

  await setNeedsVerification(
    github,
    context,
    pullRequest.number,
    (pullRequest.labels || []).map((label) => label.name),
    !result.ok,
  );

  if (!result.ok) {
    core.setFailed(result.error);
    return result;
  }

  core.info(result.skipped ? `Verification skipped: ${result.skipReason}` : 'Verification section ok.');
  return result;
}

export function main() {
  throw new Error('check-pr-verification.mjs is invoked from pr-verification-check.yml via github-script.');
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly && process.argv.includes('--self-test')) {
  // Tests live in check-pr-verification.test.mjs.
  console.log('Use: node --test scripts/check-pr-verification.test.mjs');
}
