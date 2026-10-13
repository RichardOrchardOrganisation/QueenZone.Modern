#!/usr/bin/env node
/**
 * PR Verification section check (#1799).
 *
 * UI PRs require uploaded, validated CI evidence for their current head.
 * The no-ui-verification label plus a visible reason is the only waiver.
 */
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { evaluateProof, checkProof } from './ui-proof-policy.mjs';
export { extractVerificationSection, findFeatureIds, visibleMarkdown, verificationSkipReason } from './ui-proof-policy.mjs';
import { listUiSourcePaths, loadFeatureMap, repoRootFrom, toPosix } from './check-feature-map.mjs';

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

function proofPolicy({ files = [], entries = [], mapIds = new Set(entries.map(entry => entry.id)), mobileSources = new Set(), uiPaths = null, root = repoRootFrom() } = {}) {
  const uiFiles = files.filter(file => isUiChangedPath(file, mobileSources, uiPaths ?? loadUiPaths(root)));
  const config = JSON.parse(readFileSync(path.join(root, 'development-standards.json'), 'utf8'));
  const features = [...mapIds].map(id => {
    const surface = id.startsWith('mobile.') ? 'mobile' : 'web';
    const entry = entries.find(item => item.id === id);
    return { id, platforms: config.uiProof.platforms[surface],
      fixtures: surface === 'web' ? (entry?.specs || []).map(spec => path.basename(spec, '.cs')) : [] };
  });
  const requirements = uiFiles.map(file => {
    const surface = file.startsWith('src/QueenZone.Mobile/') ? 'mobile' : 'web';
    const source = file.endsWith('.cshtml.cs') ? file.slice(0, -3) : file;
    const mapped = entries.filter(entry => entry.sources?.includes(source)).map(entry => entry.id);
    return { file, ids: features.filter(feature => feature.id.startsWith(surface + '.') && (!mapped.length || mapped.includes(feature.id))).map(feature => feature.id) };
  });
  return { features, requirements };
}

export function evaluatePrVerification(input = {}) {
  return evaluateProof({ ...input, ...proofPolicy(input) });
}

export async function checkPullRequestVerification({ root = repoRootFrom(), ...options }) {
  return checkProof({ ...options, root, loadPolicy: files => {
    const map = loadFeatureMap(root);
    return proofPolicy({ root, files, entries: map.entries, mobileSources: listUiSourcePaths(map), uiPaths: loadUiPaths(root) });
  } });
}
