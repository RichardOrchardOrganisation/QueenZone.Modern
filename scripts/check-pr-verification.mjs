#!/usr/bin/env node
/**
 * PR Verification section check (#1799).
 *
 * UI PRs require uploaded, validated CI evidence for their current head.
 * The no-ui-verification label plus a visible reason is the only waiver.
 */
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { discoverEvidence } from './ui-proof-evidence.mjs';
import { compareText, listUiSourcePaths, loadFeatureMap, repoRootFrom, toPosix } from './check-feature-map.mjs';

export const NO_UI_VERIFICATION = 'no-ui-verification';

/**
 * UI source prefixes from uiPaths in development-standards.json (#2116). A
 * trailing / is a directory prefix; anything else is one exact file. This
 * web + mobile repository must configure at least one.
 */
export function loadUiPaths(root = repoRootFrom()) {
  const config = JSON.parse(readFileSync(path.join(root, 'development-standards.json'), 'utf8'));
  const uiPaths = config.uiPaths;
  if (!Array.isArray(uiPaths) || uiPaths.length === 0) {
    throw new Error('development-standards.json must list the web and mobile uiPaths.');
  }
  for (const uiPath of uiPaths) {
    if (typeof uiPath !== 'string' || !uiPath || uiPath.startsWith('/') || uiPath.includes('\\') || uiPath.split('/').includes('..')) {
      throw new Error(`Invalid uiPaths entry in development-standards.json: ${uiPath}`);
    }
  }
  return uiPaths;
}

function matchesUiPath(posix, uiPaths) {
  return uiPaths.some((uiPath) => (uiPath.endsWith('/') ? posix.startsWith(uiPath) : posix === uiPath));
}

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
  if (posix.endsWith('Tests.cs')) {
    return true;
  }
  if (posix.startsWith('tests/') || posix.includes('/tests/')) {
    return true;
  }
  return false;
}

export function isUiChangedPath(file, mobileSources, uiPaths = loadUiPaths()) {
  const posix = toPosix(file);
  if (isIgnoredVerificationPath(posix)) {
    return false;
  }
  if (matchesUiPath(posix, uiPaths)) {
    return true;
  }
  return mobileSources.has(posix);
}

export function extractVerificationSection(body) {
  const text = String(body || '');
  const start = text.match(/^## Verification\b[^\n]*/m);
  if (!start || start.index === undefined) {
    return null;
  }
  const from = start.index;
  const afterHeading = text.slice(from + start[0].length);
  const nextHeading = afterHeading.search(/\n## /);
  return nextHeading === -1 ? text.slice(from) : text.slice(from, from + start[0].length + nextHeading);
}

export function findFeatureIds(text, ids) {
  const haystack = String(text || '');
  return [...ids].filter((id) => haystack.includes(id)).sort(compareText);
}

function removeDelimitedText(value, opening, closing) {
  let text = value;
  let start = text.indexOf(opening);
  while (start !== -1) {
    const end = text.indexOf(closing, start + opening.length);
    // Preserve a boundary so removing hidden text cannot assemble a new token.
    // An unclosed comment/fence hides the rest of the description.
    text = text.slice(0, start) + '\n' + (end === -1 ? '' : text.slice(end + closing.length));
    start = text.indexOf(opening);
  }
  return text;
}

export function visibleMarkdown(body) {
  const commentsRemoved = removeDelimitedText(String(body || ''), '<!--', '-->');
  return removeDelimitedText(commentsRemoved, '```', '```');
}

export function verificationSkipReason(body) {
  const match = visibleMarkdown(body).match(/^\s*(?:-\s*)?Verification-skip-reason:[ \t]*(\S[^\r\n]*)$/im);
  return match ? match[1].trim() : '';
}

export function evaluatePrVerification({
  body = '',
  files = [],
  labels = [],
  evidence = [],
  headSha = '',
  entries = [],
  mapIds = new Set(),
  mobileSources = new Set(),
  uiPaths = null,
} = {}) {
  const configuredUiPaths = uiPaths ?? loadUiPaths();
  const uiFiles = files.filter((file) => isUiChangedPath(file, mobileSources, configuredUiPaths));
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

  const section = extractVerificationSection(visibleMarkdown(body));
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

  for (const file of uiFiles) {
    const surface = file.startsWith('src/QueenZone.Mobile/') ? 'mobile' : 'web';
    const source = file.endsWith('.cshtml.cs') ? file.slice(0, -3) : file;
    const mapped = entries.filter(entry => entry.sources?.includes(source)).map(entry => entry.id);
    if (!ids.some(id => id.startsWith(surface + '.') && (!mapped.length || mapped.includes(id)))) {
      return { ok: false, ui, uiFiles, ids, error: `No affected feature declared for ${file}.` };
    }
  }
  const missing = [];
  for (const id of ids) {
    const platforms = id.startsWith('mobile.') ? ['android', 'ios'] : ['web'];
    const expected = entries.find(entry => entry.id === id)?.specs?.map(spec => path.basename(spec, '.cs')) || [];
    for (const platform of platforms) {
      const found = evidence.some(item => item.feature === id && item.platform === platform && item.headSha === headSha
        && item.url && (platform !== 'web' || expected.every(spec => item.fixtures?.includes(spec))));
      if (!found) missing.push(`${id}/${platform}`);
    }
  }
  if (missing.length) {
    return { ok: false, ui, uiFiles, ids, error: `Uploaded passing proof missing for current head: ${missing.join(', ')}. Local paths, arbitrary links and Not verified do not satisfy this gate.` };
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
  const input = {
    body: pullRequest.body || '', files, labels: pullRequest.labels || [], mapIds, mobileSources,
    uiPaths: loadUiPaths(root), entries: map.entries, headSha: pullRequest.head?.sha || '',
  };
  let result = evaluatePrVerification(input);
  let evidence = [];
  if (!result.ok && result.ids?.length) {
    evidence = await discoverEvidence({ github, repo: context.repo, headSha: input.headSha, root });
    result = evaluatePrVerification({ ...input, evidence });
  }
  result.evidence = evidence;
  // Label/comment writes belong only to the trusted refresh workflow.

  if (!result.ok) {
    core.setFailed(result.error);
    return result;
  }

  core.info(result.skipped ? `Verification waived: ${result.skipReason}` : 'Uploaded verification evidence checked.');
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
