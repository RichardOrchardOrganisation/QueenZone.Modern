#!/usr/bin/env node
/**
 * PR issue-link check (#1863).
 *
 * Every PR body must have at least one line matching
 * Closes / Fixes / Resolves / Part of / Relates to #N, and each N must be
 * an existing issue in this repository (not a pull request). Dependabot
 * and the `no-issue` label are exempt. Also flags implied-resolution
 * wording ("Implements #N") that will not auto-close an issue.
 *
 * Invoked from .github/workflows/pr-issue-link-check.yml via github-script.
 */
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { isDependabot } from './check-pr-verification.mjs';

export const NO_ISSUE_LABEL = 'no-issue';

export const ISSUE_LINK_PATTERN = /\b(closes|fixes|resolves|part of|relates to)\s+#(\d+)\b/gi;

export const GITHUB_CLOSING_KEYWORD_PATTERN = /(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)/gi;

export const IMPLIED_RESOLUTION_PATTERN =
  /\b(?:implements?|addresses?|completes?|delivers?|finishes?)\b[^.\n]{0,40}?#(\d+)/gi;

export const MISSING_LINK_MESSAGE =
  'PR description is missing a required issue link. ' +
  'Add at least one line matching Closes #N, Fixes #N, Resolves #N, Part of #N, or Relates to #N (case-insensitive). ' +
  'Closes, Fixes, and Resolves close the issue on merge. Part of and Relates to do not. ' +
  'Each N must be an existing issue in this repository, not a pull request. ' +
  'Issue-less PRs: apply the no-issue label (Dependabot is exempt). ' +
  'See AGENTS.md > "Linking issues so merge auto-closes them".';

export function stripHtmlComments(body) {
  return String(body || '').replace(/<!--[\s\S]*?-->/g, '');
}

export function labelNames(labels = []) {
  return labels.map((label) => (typeof label === 'string' ? label : label?.name)).filter(Boolean);
}

export function extractIssueLinks(body) {
  const text = stripHtmlComments(body);
  const pattern = new RegExp(ISSUE_LINK_PATTERN.source, 'gi');
  const links = [];
  for (const match of text.matchAll(pattern)) {
    links.push({
      keyword: match[1].toLowerCase(),
      number: Number(match[2]),
    });
  }
  return links;
}

function impliedResolutionWithoutClosingKeyword(text) {
  const closedIssueNumbers = new Set();
  const closingPattern = new RegExp(GITHUB_CLOSING_KEYWORD_PATTERN.source, 'gi');
  for (const match of text.matchAll(closingPattern)) {
    closedIssueNumbers.add(match[1]);
  }

  const missing = new Set();
  const impliedPattern = new RegExp(IMPLIED_RESOLUTION_PATTERN.source, 'gi');
  for (const match of text.matchAll(impliedPattern)) {
    if (!closedIssueNumbers.has(match[1])) {
      missing.add(match[1]);
    }
  }
  return [...missing];
}

export function evaluateIssueLinks({
  body = '',
  labels = [],
  dependabot = false,
  issuesByNumber = new Map(),
} = {}) {
  if (dependabot) {
    return { ok: true, exemption: 'Dependabot' };
  }

  if (labelNames(labels).includes(NO_ISSUE_LABEL)) {
    return { ok: true, exemption: 'no-issue label' };
  }

  const text = stripHtmlComments(body);
  const links = extractIssueLinks(text);
  if (links.length === 0) {
    return { ok: false, error: MISSING_LINK_MESSAGE };
  }

  const uniqueNumbers = [...new Set(links.map((link) => link.number))];
  for (const number of uniqueNumbers) {
    const issue = issuesByNumber.get(number);
    if (!issue || !issue.exists) {
      return {
        ok: false,
        links,
        error:
          `#${number} is not an existing issue in this repository. ` +
          'Each linked N must be an existing issue, not a pull request. ' +
          'Closes, Fixes, and Resolves close the issue on merge. Part of and Relates to do not.',
      };
    }
    if (issue.isPullRequest) {
      return {
        ok: false,
        links,
        error:
          `#${number} is a pull request, not an issue. ` +
          'Link an existing issue with Closes #N, Fixes #N, Resolves #N, Part of #N, or Relates to #N. ' +
          'Closes, Fixes, and Resolves close the issue on merge. Part of and Relates to do not.',
      };
    }
  }

  const missingKeyword = impliedResolutionWithoutClosingKeyword(text);
  if (missingKeyword.length > 0) {
    const list = missingKeyword.map((n) => `#${n}`).join(', ');
    return {
      ok: false,
      links,
      error:
        `PR body describes resolving ${list} (e.g. "Implements #N") without a GitHub ` +
        `closing keyword, so merging this PR will NOT auto-close ${list}. ` +
        `Add "Closes #N" / "Fixes #N" / "Resolves #N" if this PR fully resolves the issue. ` +
        `Closes, Fixes, and Resolves close the issue on merge. Part of and Relates to do not. ` +
        `See AGENTS.md > "Linking issues so merge auto-closes them".`,
    };
  }

  return { ok: true, links };
}

export async function fetchIssue(github, context, number) {
  try {
    const { data } = await github.rest.issues.get({
      owner: context.repo.owner,
      repo: context.repo.repo,
      issue_number: number,
    });
    return {
      number,
      exists: true,
      isPullRequest: Boolean(data.pull_request),
    };
  } catch (error) {
    if (error.status === 404) {
      return { number, exists: false, isPullRequest: false };
    }
    throw error;
  }
}

export async function checkPullRequestIssueLink({ github, context, core }) {
  if (context.eventName === 'merge_group') {
    core.info('Passing trivially on merge_group; issue links were already checked on the pull request.');
    return { ok: true, skipped: 'merge_group' };
  }

  const pullRequest = context.payload.pull_request;
  if (!pullRequest) {
    core.info('No pull request payload; skipping issue link check.');
    return { ok: true, skipped: true };
  }

  const dependabot = isDependabot(pullRequest);
  const labels = pullRequest.labels || [];
  const body = pullRequest.body || '';

  if (dependabot || labelNames(labels).includes(NO_ISSUE_LABEL)) {
    const result = evaluateIssueLinks({ body, labels, dependabot });
    core.info(`${result.exemption} exemption applied.`);
    return result;
  }

  const links = extractIssueLinks(body);
  const issuesByNumber = new Map();
  for (const number of new Set(links.map((link) => link.number))) {
    issuesByNumber.set(number, await fetchIssue(github, context, number));
  }

  const result = evaluateIssueLinks({
    body,
    labels,
    dependabot,
    issuesByNumber,
  });

  if (!result.ok) {
    core.setFailed(result.error);
    return result;
  }

  const summary = result.links.map((link) => `${link.keyword} #${link.number}`).join(', ');
  core.info(`Issue link ok: ${summary}`);
  return result;
}

export function main() {
  throw new Error('check-pr-issue-link.mjs is invoked from pr-issue-link-check.yml via github-script.');
}

const invokedDirectly = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (invokedDirectly && process.argv.includes('--self-test')) {
  console.log('Use: node --test scripts/check-pr-issue-link.test.mjs');
}
