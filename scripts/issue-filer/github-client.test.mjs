import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGitHubClient, parseRepository } from './github-client.mjs';

function jsonResponse(data, { status = 200, headers = {} } = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get(name) {
        return headers[name.toLowerCase()] || headers[name] || null;
      },
    },
    async text() {
      return data == null ? '' : JSON.stringify(data);
    },
  };
}

test('parseRepository requires owner/repo', () => {
  assert.deepEqual(parseRepository('acme/widgets'), {
    owner: 'acme',
    repo: 'widgets',
  });
  assert.throws(() => parseRepository('nonesuch'), /owner\/repo/);
});

test('listIssuesByLabel paginates, skips pull requests, and normalizes fields', async () => {
  const calls = [];
  const fetchImpl = async (url) => {
    calls.push(url);
    if (String(url).includes('page=2') || String(url).includes('after=1')) {
      return jsonResponse([
        { number: 2, title: 'two', body: '', state: 'open', user: { login: 'bot' }, labels: [{ name: 'gardener' }] },
      ]);
    }
    return jsonResponse(
      [
        {
          number: 1,
          title: 'one',
          body: 'x',
          state: 'closed',
          state_reason: 'completed',
          closed_at: '2026-09-01T00:00:00Z',
          created_at: '2026-08-01T00:00:00Z',
          user: { login: 'github-actions[bot]' },
          labels: ['gardener'],
        },
        { number: 9, title: 'pr', pull_request: {}, user: { login: 'x' }, labels: [], state: 'open' },
      ],
      { headers: { link: '<https://api.github.com/repos/o/r/issues?page=2>; rel="next"' } },
    );
  };
  const client = createGitHubClient({ token: 't', owner: 'o', repo: 'r', fetchImpl });
  const issues = await client.listIssuesByLabel('gardener', { since: '2026-07-01T00:00:00Z' });
  assert.equal(issues.length, 2);
  assert.equal(issues[0].stateReason, 'completed');
  assert.equal(issues[0].user, 'github-actions[bot]');
  assert.equal(calls.length, 2);
});

test('write helpers post create, comment, reopen, and labels', async () => {
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : null });
    if (init.method === 'PATCH') {
      return jsonResponse({ number: 4, title: 'reopen', state: 'open', user: { login: 'bot' }, labels: [] });
    }
    if (String(url).endsWith('/issues')) {
      return jsonResponse({ number: 3, title: 'new', state: 'open', user: { login: 'bot' }, labels: [{ name: 'gardener' }] });
    }
    return jsonResponse({ id: 1 });
  };
  const client = createGitHubClient({ token: 't', owner: 'o', repo: 'r', fetchImpl });
  const created = await client.createIssue({ title: 't', body: 'b', labels: ['gardener'] });
  assert.equal(created.number, 3);
  await client.comment(3, 'hi');
  await client.reopen(4);
  await client.addLabels(4, ['regression']);
  assert.deepEqual(calls.map((item) => item.method), ['POST', 'POST', 'PATCH', 'POST']);
});

test('ensureLabel creates on 404 and reraises other errors', async () => {
  let gets = 0;
  const fetchImpl = async (url, init) => {
    if (init.method === 'GET') {
      gets += 1;
      if (gets === 1) {
        return jsonResponse({ message: 'no' }, { status: 404 });
      }
      return jsonResponse({ name: 'gardener' });
    }
    return jsonResponse({ name: 'gardener' });
  };
  const client = createGitHubClient({ token: 't', owner: 'o', repo: 'r', fetchImpl });
  assert.equal((await client.ensureLabel('gardener', '0e8a16', 'x')).created, true);
  assert.equal((await client.ensureLabel('gardener', '0e8a16', 'x')).created, false);

  const failing = createGitHubClient({
    token: 't',
    owner: 'o',
    repo: 'r',
    fetchImpl: async () => jsonResponse({ message: 'nope' }, { status: 500 }),
  });
  await assert.rejects(() => failing.ensureLabel('x', '000000', 'x'), /500/);
});
