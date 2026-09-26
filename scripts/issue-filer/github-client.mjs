const API_VERSION = '2022-11-28';
const DEFAULT_TIMEOUT_MS = 20_000;

function normalizeIssue(issue) {
  return {
    number: issue.number,
    title: issue.title,
    body: issue.body || '',
    state: issue.state,
    stateReason: issue.state_reason || issue.stateReason || '',
    closedAt: issue.closed_at || issue.closedAt || null,
    createdAt: issue.created_at || issue.createdAt,
    updatedAt: issue.updated_at || issue.updatedAt,
    user: issue.user?.login || issue.user || '',
    labels: (issue.labels || []).map((label) => (typeof label === 'string' ? label : label.name)),
    htmlUrl: issue.html_url || issue.htmlUrl || '',
    pullRequest: Boolean(issue.pull_request || issue.pullRequest),
    lastFilerCommentAt: issue.lastFilerCommentAt || null,
  };
}

export function createGitHubClient({
  token,
  owner,
  repo,
  fetchImpl = fetch,
  timeoutMs = DEFAULT_TIMEOUT_MS,
} = {}) {
  if (!owner || !repo) {
    throw new Error('createGitHubClient requires owner and repo');
  }

  async function request(method, urlPath, body) {
    const url = urlPath.startsWith('http') ? urlPath : `https://api.github.com${urlPath}`;
    const headers = {
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': API_VERSION,
      'User-Agent': 'queenzone-issue-filer',
    };
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    const init = { method, headers, signal: AbortSignal.timeout(timeoutMs) };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }
    const response = await fetchImpl(url, init);
    if (response.status === 204) {
      return { data: null, headers: response.headers, status: 204 };
    }
    const text = await response.text();
    const data = text ? JSON.parse(text) : null;
    if (!response.ok) {
      const error = new Error(`GitHub ${method} ${urlPath} failed: ${response.status}`);
      error.status = response.status;
      error.data = data;
      throw error;
    }
    return { data, headers: response.headers, status: response.status };
  }

  async function paginate(urlPath) {
    const items = [];
    let next = urlPath;
    while (next) {
      const { data, headers } = await request('GET', next);
      items.push(...(Array.isArray(data) ? data : []));
      const link = headers.get?.('link') || headers.Link || headers.link || '';
      const match = String(link).match(/<([^>]+)>;\s*rel="next"/);
      next = match ? match[1] : '';
    }
    return items;
  }

  return {
    async listIssuesByLabel(label, { state = 'all', since } = {}) {
      const query = new URLSearchParams({
        labels: label,
        state,
        per_page: '100',
      });
      if (since) {
        query.set('since', since);
      }
      const items = await paginate(`/repos/${owner}/${repo}/issues?${query}`);
      return items.filter((issue) => !issue.pull_request).map(normalizeIssue);
    },

    async createIssue({ title, body, labels }) {
      const { data } = await request('POST', `/repos/${owner}/${repo}/issues`, { title, body, labels });
      return normalizeIssue(data);
    },

    async comment(issueNumber, body) {
      const { data } = await request('POST', `/repos/${owner}/${repo}/issues/${issueNumber}/comments`, { body });
      return data;
    },

    async reopen(issueNumber) {
      const { data } = await request('PATCH', `/repos/${owner}/${repo}/issues/${issueNumber}`, { state: 'open' });
      return normalizeIssue(data);
    },

    async addLabels(issueNumber, labels) {
      const { data } = await request('POST', `/repos/${owner}/${repo}/issues/${issueNumber}/labels`, { labels });
      return data;
    },

    async listIssueComments(issueNumber) {
      return paginate(`/repos/${owner}/${repo}/issues/${issueNumber}/comments?per_page=100`);
    },

    async listPullRequests({ state = 'all', sort = 'updated', direction = 'desc' } = {}) {
      const query = new URLSearchParams({ state, sort, direction, per_page: '100' });
      return paginate(`/repos/${owner}/${repo}/pulls?${query}`);
    },

    async listReviewComments(pullNumber) {
      return paginate(`/repos/${owner}/${repo}/pulls/${pullNumber}/comments?per_page=100`);
    },

    async listReviews(pullNumber) {
      return paginate(`/repos/${owner}/${repo}/pulls/${pullNumber}/reviews?per_page=100`);
    },

    async listWorkflowRuns({ status, branch, event } = {}) {
      const query = new URLSearchParams({ per_page: '100' });
      if (status) {
        query.set('status', status);
      }
      if (branch) {
        query.set('branch', branch);
      }
      if (event) {
        query.set('event', event);
      }
      const items = [];
      let next = `/repos/${owner}/${repo}/actions/runs?${query}`;
      while (next) {
        const { data, headers } = await request('GET', next);
        items.push(...(data.workflow_runs || []));
        const link = headers.get?.('link') || headers.Link || headers.link || '';
        const match = String(link).match(/<([^>]+)>;\s*rel="next"/);
        next = match ? match[1] : '';
        if (items.length >= 200) {
          break;
        }
      }
      return items;
    },

    async listJobsForRun(runId) {
      const { data } = await request('GET', `/repos/${owner}/${repo}/actions/runs/${runId}/jobs?per_page=100`);
      return data.jobs || [];
    },

    async ensureLabel(name, color, description) {
      try {
        await request('GET', `/repos/${owner}/${repo}/labels/${encodeURIComponent(name)}`);
        return { created: false, name };
      } catch (error) {
        if (error.status !== 404) {
          throw error;
        }
        await request('POST', `/repos/${owner}/${repo}/labels`, { name, color, description });
        return { created: true, name };
      }
    },

    async findOpenIssueByTitle(title) {
      const items = await paginate(`/repos/${owner}/${repo}/issues?state=open&per_page=100`);
      const hit = items.find((issue) => !issue.pull_request && issue.title === title);
      return hit ? normalizeIssue(hit) : null;
    },
  };
}

export function parseRepository(value) {
  const [owner, repo] = String(value || '').split('/');
  if (!owner || !repo) {
    throw new Error('GITHUB_REPOSITORY must be owner/repo');
  }
  return { owner, repo };
}
