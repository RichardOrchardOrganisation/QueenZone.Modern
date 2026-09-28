import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  MISSING_LINK_MESSAGE,
  checkPullRequestIssueLink,
  evaluateIssueLinks,
  extractIssueLinks,
  fetchIssue,
  stripHtmlComments,
} from './check-pr-issue-link.mjs';

function issue(number, { isPullRequest = false } = {}) {
  return { number, exists: true, isPullRequest };
}

function issues(...numbers) {
  return new Map(numbers.map((number) => [number, issue(number)]));
}

function mockCore() {
  const messages = { info: [], failed: [] };
  return {
    messages,
    info(message) {
      messages.info.push(String(message));
    },
    setFailed(message) {
      messages.failed.push(String(message));
    },
  };
}

function mockGithub(catalog) {
  return {
    rest: {
      issues: {
        async get({ issue_number }) {
          const row = catalog[issue_number];
          if (!row) {
            const error = new Error('Not Found');
            error.status = 404;
            throw error;
          }
          return { data: row };
        },
      },
    },
  };
}

test('HTML comments are stripped so template examples do not count', () => {
  const body = '## Issue\n\n<!-- Closes #123 -->\n\n';
  assert.equal(stripHtmlComments(body).includes('Closes #123'), false);
  assert.deepEqual(extractIssueLinks(body), []);
});

test('extracts each recognized keyword case-insensitively', () => {
  const body = ['Closes #10', 'fixes #11', 'RESOLVES #12', 'Part of #13', 'relates to #14'].join('\n');
  assert.deepEqual(extractIssueLinks(body), [
    { keyword: 'closes', number: 10 },
    { keyword: 'fixes', number: 11 },
    { keyword: 'resolves', number: 12 },
    { keyword: 'part of', number: 13 },
    { keyword: 'relates to', number: 14 },
  ]);
});

test('fails when the body has no recognized issue-link line', () => {
  const result = evaluateIssueLinks({
    body: '## Summary\nImplements the thing.\n',
    issuesByNumber: issues(1863),
  });
  assert.equal(result.ok, false);
  assert.equal(result.error, MISSING_LINK_MESSAGE);
  assert.match(result.error, /Closes, Fixes, and Resolves close the issue on merge/);
  assert.match(result.error, /Part of and Relates to do not/);
});

test('prose Implements #N without a keyword line fails the missing-link rule', () => {
  const result = evaluateIssueLinks({
    body: 'Implements #1863 in the summary.\n',
    issuesByNumber: issues(1863),
  });
  assert.equal(result.ok, false);
  assert.equal(result.error, MISSING_LINK_MESSAGE);
});

test('Closes #N passes when N is an existing issue', () => {
  const result = evaluateIssueLinks({
    body: '## Issue\n\nCloses #1863\n',
    issuesByNumber: issues(1863),
  });
  assert.equal(result.ok, true);
  assert.deepEqual(result.links, [{ keyword: 'closes', number: 1863 }]);
});

test('Part of and Relates to pass without closing the issue', () => {
  assert.equal(
    evaluateIssueLinks({
      body: 'Part of #1805\n',
      issuesByNumber: issues(1805),
    }).ok,
    true,
  );
  assert.equal(
    evaluateIssueLinks({
      body: 'Relates to #42\n',
      issuesByNumber: issues(42),
    }).ok,
    true,
  );
});

test('missing and pull-request numbers fail', () => {
  const missing = evaluateIssueLinks({
    body: 'Closes #999999\n',
    issuesByNumber: new Map([[999999, { number: 999999, exists: false, isPullRequest: false }]]),
  });
  assert.equal(missing.ok, false);
  assert.match(missing.error, /#999999 is not an existing issue/);
  assert.match(missing.error, /Part of and Relates to do not/);

  const prNumber = evaluateIssueLinks({
    body: 'Closes #1865\n',
    issuesByNumber: new Map([[1865, issue(1865, { isPullRequest: true })]]),
  });
  assert.equal(prNumber.ok, false);
  assert.match(prNumber.error, /#1865 is a pull request, not an issue/);
});

test('Dependabot and no-issue exemptions pass and name which one applied', () => {
  const bot = evaluateIssueLinks({
    body: '',
    dependabot: true,
  });
  assert.equal(bot.ok, true);
  assert.equal(bot.exemption, 'Dependabot');

  const labeled = evaluateIssueLinks({
    body: '',
    labels: [{ name: 'no-issue' }],
  });
  assert.equal(labeled.ok, true);
  assert.equal(labeled.exemption, 'no-issue label');
});

test('Implements #N still fails unless a GitHub closing keyword is also present', () => {
  const implied = evaluateIssueLinks({
    body: 'Implements #20\n\nPart of #10\n',
    issuesByNumber: issues(10),
  });
  assert.equal(implied.ok, false);
  assert.match(implied.error, /Implements #N/);
  assert.match(implied.error, /will NOT auto-close #20/);
  assert.match(implied.error, /Part of and Relates to do not/);

  const closed = evaluateIssueLinks({
    body: 'Implements #20\n\nCloses #20\n',
    issuesByNumber: issues(20),
  });
  assert.equal(closed.ok, true);
});

test('checkPullRequestIssueLink passes trivially on merge_group', async () => {
  const core = mockCore();
  const result = await checkPullRequestIssueLink({
    github: mockGithub({}),
    context: { eventName: 'merge_group', payload: {}, repo: { owner: 'o', repo: 'r' } },
    core,
  });
  assert.equal(result.ok, true);
  assert.equal(result.skipped, 'merge_group');
  assert.match(core.messages.info.join('\n'), /Passing trivially on merge_group/);
  assert.deepEqual(core.messages.failed, []);
});

test('checkPullRequestIssueLink reports Dependabot and no-issue exemptions', async () => {
  const botCore = mockCore();
  const bot = await checkPullRequestIssueLink({
    github: mockGithub({}),
    context: {
      eventName: 'pull_request',
      repo: { owner: 'o', repo: 'r' },
      payload: {
        pull_request: {
          body: '',
          labels: [],
          user: { login: 'dependabot[bot]' },
          head: { ref: 'dependabot/nuget/example' },
        },
      },
    },
    core: botCore,
  });
  assert.equal(bot.exemption, 'Dependabot');
  assert.match(botCore.messages.info.join('\n'), /Dependabot exemption applied/);

  const labelCore = mockCore();
  const labeled = await checkPullRequestIssueLink({
    github: mockGithub({}),
    context: {
      eventName: 'pull_request',
      repo: { owner: 'o', repo: 'r' },
      payload: {
        pull_request: {
          body: '',
          labels: [{ name: 'no-issue' }],
          user: { login: 'cursor' },
          head: { ref: 'cursor/example' },
        },
      },
    },
    core: labelCore,
  });
  assert.equal(labeled.exemption, 'no-issue label');
  assert.match(labelCore.messages.info.join('\n'), /no-issue label exemption applied/);
});

test('checkPullRequestIssueLink fetches issues and fails for a PR number', async () => {
  const core = mockCore();
  const result = await checkPullRequestIssueLink({
    github: mockGithub({
      1863: { pull_request: { url: 'https://example.test/pr/1863' } },
    }),
    context: {
      eventName: 'pull_request',
      repo: { owner: 'o', repo: 'r' },
      payload: {
        pull_request: {
          body: 'Closes #1863\n',
          labels: [],
          user: { login: 'cursor' },
          head: { ref: 'cursor/example' },
        },
      },
    },
    core,
  });
  assert.equal(result.ok, false);
  assert.match(core.messages.failed.join('\n'), /#1863 is a pull request/);
});

test('checkPullRequestIssueLink accepts an existing issue', async () => {
  const core = mockCore();
  const result = await checkPullRequestIssueLink({
    github: mockGithub({
      1863: { title: 'Require an issue link' },
    }),
    context: {
      eventName: 'pull_request',
      repo: { owner: 'o', repo: 'r' },
      payload: {
        pull_request: {
          body: 'Closes #1863\n',
          labels: [],
          user: { login: 'cursor' },
          head: { ref: 'cursor/example' },
        },
      },
    },
    core,
  });
  assert.equal(result.ok, true);
  assert.match(core.messages.info.join('\n'), /Issue link ok: closes #1863/);
});

test('fetchIssue maps 404 and pull_request payloads', async () => {
  const github = mockGithub({
    1: { title: 'issue' },
    2: { pull_request: { url: 'https://example.test/pr/2' } },
  });
  const context = { repo: { owner: 'o', repo: 'r' } };
  assert.deepEqual(await fetchIssue(github, context, 1), { number: 1, exists: true, isPullRequest: false });
  assert.deepEqual(await fetchIssue(github, context, 2), { number: 2, exists: true, isPullRequest: true });
  assert.deepEqual(await fetchIssue(github, context, 3), { number: 3, exists: false, isPullRequest: false });
});
