#!/usr/bin/env node
/**
 * GitHub-native release notes for vYYYY.MM.DD.N tags and the Unreleased draft (#2379).
 *
 * Calls POST /releases/generate-notes, then appends `(closes #N)` from each
 * pull request's closingIssuesReferences. Category grouping lives in
 * `.github/release.yml`.
 */
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const PROD_TAG_RE = /^v(\d{4})\.(\d{2})\.(\d{2})\.(\d+)$/;
export const UNRELEASED_NAME = 'Unreleased';
export const UNRELEASED_TAG = 'unreleased';
export const PATH_CATEGORY_LABELS = Object.freeze(['mobile', 'infra', 'dependencies']);
export const CATEGORY_LABELS = Object.freeze({
  feature: { color: '0E8A16', description: 'User-facing feature. Release notes: Features.' },
  fix: { color: 'D93F0B', description: 'Bug fix. Release notes: Fixes.' },
  mobile: { color: '1D76DB', description: 'Mobile app change. Release notes: Mobile.' },
  infra: { color: '5319E7', description: 'Workflows or infra. Release notes: Infra/CI.' },
  dependencies: { color: '0366d6', description: 'Dependency update. Release notes: Dependencies.' },
});

export function isProdTag(name) {
  return PROD_TAG_RE.test(String(name || ''));
}

export function parseProdTag(name) {
  const match = String(name || '').match(PROD_TAG_RE);
  if (!match) {
    return null;
  }
  return {
    name: String(name),
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
    n: Number(match[4]),
  };
}

export function compareProdTags(left, right) {
  const first = parseProdTag(left);
  const second = parseProdTag(right);
  if (!first || !second) {
    throw new Error(`compareProdTags requires prod tags, got ${JSON.stringify(left)} and ${JSON.stringify(right)}`);
  }
  return first.year - second.year || first.month - second.month || first.day - second.day || first.n - second.n;
}

export function listProdTags(names) {
  return [...new Set((names || []).map((name) => String(name || '')).filter(isProdTag))]
    .sort(compareProdTags);
}

export function previousProdTag(names, current) {
  const prod = listProdTags(names);
  if (!current) {
    return prod.at(-1) || null;
  }
  if (!isProdTag(current)) {
    throw new Error(`Not a prod tag: ${JSON.stringify(current)}`);
  }
  const older = prod.filter((tag) => compareProdTags(tag, current) < 0);
  return older.at(-1) || null;
}

export function isLatestProdTag(names, tag) {
  const latest = previousProdTag(names, null);
  return Boolean(latest) && compareProdTags(tag, latest) >= 0;
}

export function tagFromRef(ref) {
  return String(ref || '').replace(/^refs\/tags\//, '');
}

export function extractPullNumbers(body) {
  const numbers = new Set();
  for (const match of String(body || '').matchAll(/\/pull\/(\d+)/g)) {
    numbers.add(Number(match[1]));
  }
  return [...numbers];
}

export function formatCloses(issueNumbers) {
  const unique = [...new Set(issueNumbers)]
    .filter((value) => Number.isInteger(value) && value > 0)
    .sort((left, right) => left - right);
  if (unique.length === 0) {
    return '';
  }
  return `(closes ${unique.map((number) => `#${number}`).join(', ')})`;
}

export function appendCloses(body, closesByPull) {
  const lookup = closesByPull instanceof Map
    ? closesByPull
    : new Map(Object.entries(closesByPull || {}).map(([key, value]) => [Number(key), value]));
  return String(body || '').split('\n').map((line) => {
    const pulls = extractPullNumbers(line);
    if (pulls.length === 0) {
      return line;
    }
    const issues = [];
    for (const pull of pulls) {
      issues.push(...(lookup.get(pull) || []));
    }
    const suffix = formatCloses(issues);
    if (!suffix || line.includes(suffix) || /\(closes #\d+/.test(line)) {
      return line;
    }
    return `${line.replace(/\s+$/, '')} ${suffix}`;
  }).join('\n');
}

export function labelsFromTitle(title) {
  const text = String(title || '').trim();
  if (/^(feat|feature)(\b|[(:/])/i.test(text)) {
    return ['feature'];
  }
  if (/^(fix|bugfix|bug)(\b|[(:/])/i.test(text)) {
    return ['fix'];
  }
  return [];
}

export function titleLabelsToApply(title, existingLabels) {
  const names = new Set((existingLabels || []).map((label) => (typeof label === 'string' ? label : label.name)));
  if (PATH_CATEGORY_LABELS.some((label) => names.has(label))) {
    return [];
  }
  return labelsFromTitle(title).filter((label) => !names.has(label));
}

export function parseCliArgs(argv) {
  const args = [...(argv || [])];
  const command = args.find((value) => value && !value.startsWith('-')) || '';
  const tagIndex = args.indexOf('--tag');
  return {
    command,
    dryRun: args.includes('--dry-run'),
    tag: tagIndex >= 0 ? String(args[tagIndex + 1] || '') : '',
  };
}

function githubError(method, requestPath, status, text) {
  const error = new Error(`GitHub ${method} ${requestPath} failed: ${status} ${text}`);
  error.status = status;
  return error;
}

export function createGithubClient({ token, repo, fetchImpl = fetch }) {
  if (!token) {
    throw new Error('GITHUB_TOKEN is required.');
  }
  if (!repo || !repo.includes('/')) {
    throw new Error(`GITHUB_REPOSITORY is required, got ${JSON.stringify(repo)}`);
  }
  const [owner, name] = repo.split('/');
  const api = 'https://api.github.com';

  async function request(requestPath, { method = 'GET', body } = {}) {
    const response = await fetchImpl(`${api}${requestPath}`, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      throw githubError(method, requestPath, response.status, await response.text());
    }
    if (response.status === 204) {
      return null;
    }
    return response.json();
  }

  async function listAll(requestPath) {
    const items = [];
    const joiner = requestPath.includes('?') ? '&' : '?';
    for (let page = 1; page <= 20; page += 1) {
      const batch = await request(`${requestPath}${joiner}per_page=100&page=${page}`);
      items.push(...batch);
      if (batch.length < 100) {
        break;
      }
    }
    return items;
  }

  return {
    owner,
    name,
    repo,
    async listTagNames() {
      const tags = await listAll(`/repos/${owner}/${name}/tags`);
      return tags.map((tag) => tag.name);
    },
    async generateNotes(payload) {
      return request(`/repos/${owner}/${name}/releases/generate-notes`, { method: 'POST', body: payload });
    },
    async graphql(query, variables) {
      const payload = await request('/graphql', { method: 'POST', body: { query, variables } });
      if (payload.errors?.length) {
        throw new Error(payload.errors.map((entry) => entry.message).join('; '));
      }
      return payload.data;
    },
    async closingIssuesForPulls(numbers) {
      const unique = [...new Set(numbers)].filter((value) => Number.isInteger(value) && value > 0);
      const result = new Map();
      for (let index = 0; index < unique.length; index += 10) {
        const chunk = unique.slice(index, index + 10);
        const fields = chunk.map((number) => `p${number}: pullRequest(number: ${number}) { closingIssuesReferences(first: 20) { nodes { number } } }`).join('\n');
        const data = await this.graphql(
          `query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { ${fields} } }`,
          { owner, name },
        );
        for (const number of chunk) {
          const nodes = data?.repository?.[`p${number}`]?.closingIssuesReferences?.nodes || [];
          result.set(number, nodes.map((node) => node.number).filter((value) => Number.isInteger(value)));
        }
      }
      return result;
    },
    async findReleaseByTag(tag) {
      try {
        return await request(`/repos/${owner}/${name}/releases/tags/${encodeURIComponent(tag)}`);
      } catch (error) {
        if (error.status === 404) {
          return null;
        }
        throw error;
      }
    },
    async listReleases() {
      return listAll(`/repos/${owner}/${name}/releases`);
    },
    async findUnreleasedDrafts() {
      const releases = await this.listReleases();
      return releases.filter((release) => release.draft && (release.name === UNRELEASED_NAME || release.tag_name === UNRELEASED_TAG));
    },
    async createRelease(payload) {
      return request(`/repos/${owner}/${name}/releases`, { method: 'POST', body: payload });
    },
    async updateRelease(id, payload) {
      return request(`/repos/${owner}/${name}/releases/${id}`, { method: 'PATCH', body: payload });
    },
    async deleteRelease(id) {
      return request(`/repos/${owner}/${name}/releases/${id}`, { method: 'DELETE' });
    },
    async deleteUnreleasedDrafts() {
      const drafts = await this.findUnreleasedDrafts();
      for (const draft of drafts) {
        await this.deleteRelease(draft.id);
      }
      return drafts.length;
    },
    async getPull(number) {
      return request(`/repos/${owner}/${name}/pulls/${number}`);
    },
    async getLabel(labelName) {
      return request(`/repos/${owner}/${name}/labels/${encodeURIComponent(labelName)}`);
    },
    async createLabel(payload) {
      return request(`/repos/${owner}/${name}/labels`, { method: 'POST', body: payload });
    },
    async addLabels(number, labels) {
      return request(`/repos/${owner}/${name}/issues/${number}/labels`, { method: 'POST', body: { labels } });
    },
  };
}

export async function generateReleaseBody(github, { tagName, previousTag, targetCommitish }) {
  const payload = { tag_name: tagName };
  if (previousTag) {
    payload.previous_tag_name = previousTag;
  }
  if (targetCommitish) {
    payload.target_commitish = targetCommitish;
  }
  const generated = await github.generateNotes(payload);
  const closes = await github.closingIssuesForPulls(extractPullNumbers(generated.body || ''));
  return {
    name: generated.name,
    body: appendCloses(generated.body || '', closes),
  };
}

export async function publishTagRelease(github, { tag, deleteUnreleased }) {
  if (!isProdTag(tag)) {
    throw new Error(`Expected a prod tag vYYYY.MM.DD.N, got ${JSON.stringify(tag)}`);
  }
  const tags = await github.listTagNames();
  const previous = previousProdTag(tags, tag);
  const notes = await generateReleaseBody(github, { tagName: tag, previousTag: previous });
  const latest = isLatestProdTag([...tags, tag], tag);
  const payload = {
    tag_name: tag,
    name: tag,
    body: notes.body,
    draft: false,
    prerelease: false,
    make_latest: latest ? 'true' : 'false',
  };
  const existing = await github.findReleaseByTag(tag);
  const release = existing
    ? await github.updateRelease(existing.id, payload)
    : await github.createRelease(payload);
  if (deleteUnreleased) {
    await github.deleteUnreleasedDrafts();
  }
  return { release, previous, body: notes.body };
}

export async function upsertUnreleasedDraft(github) {
  const tags = await github.listTagNames();
  const previous = previousProdTag(tags, null);
  const notes = await generateReleaseBody(github, {
    tagName: UNRELEASED_TAG,
    previousTag: previous,
    targetCommitish: 'main',
  });
  const payload = {
    tag_name: UNRELEASED_TAG,
    name: UNRELEASED_NAME,
    body: notes.body,
    draft: true,
    prerelease: false,
    target_commitish: 'main',
    make_latest: 'false',
  };
  const [primary, ...duplicates] = await github.findUnreleasedDrafts();
  const release = primary
    ? await github.updateRelease(primary.id, payload)
    : await github.createRelease(payload);
  for (const duplicate of duplicates) {
    await github.deleteRelease(duplicate.id);
  }
  return { release, previous, body: notes.body };
}

export async function ensureLabel(github, labelName) {
  const spec = CATEGORY_LABELS[labelName];
  if (!spec) {
    throw new Error(`Unknown category label: ${labelName}`);
  }
  try {
    await github.getLabel(labelName);
  } catch (error) {
    if (error.status !== 404) {
      throw error;
    }
    await github.createLabel({ name: labelName, color: spec.color, description: spec.description });
  }
}

export async function applyTitleLabels(github, { number, title, existingLabels }) {
  const pullNumber = Number(number);
  if (!Number.isInteger(pullNumber) || pullNumber <= 0) {
    throw new Error(`PR_NUMBER is required, got ${JSON.stringify(number)}`);
  }
  const labels = existingLabels || (await github.getPull(pullNumber)).labels || [];
  const add = titleLabelsToApply(title, labels);
  for (const label of add) {
    await ensureLabel(github, label);
  }
  if (add.length) {
    await github.addLabels(pullNumber, add);
  }
  return { number: pullNumber, added: add };
}

export async function dryRunNotes(github, { tag }) {
  const tags = await github.listTagNames();
  const previous = tag ? previousProdTag(tags, tag) : previousProdTag(tags, null);
  return {
    previous,
    ...await generateReleaseBody(github, {
      tagName: tag || UNRELEASED_TAG,
      previousTag: previous,
      targetCommitish: tag ? undefined : 'main',
    }),
  };
}

export async function main(argv = process.argv.slice(2), env = process.env, fetchImpl = fetch) {
  const options = parseCliArgs(argv);
  const github = createGithubClient({
    token: env.GH_TOKEN || env.GITHUB_TOKEN,
    repo: env.GITHUB_REPOSITORY,
    fetchImpl,
  });

  if (options.command === 'apply-title-labels') {
    const result = await applyTitleLabels(github, {
      number: env.PR_NUMBER,
      title: env.PR_TITLE,
    });
    console.log(result.added.length ? `Applied ${result.added.join(', ')}.` : 'No title labels to apply.');
    return result;
  }

  const tag = options.tag || env.RELEASE_TAG || (options.command === 'draft' ? '' : tagFromRef(env.GITHUB_REF));
  if (options.dryRun) {
    const result = await dryRunNotes(github, { tag: options.command === 'draft' ? '' : tag });
    console.log(JSON.stringify({ previous: result.previous, name: result.name, body: result.body }, null, 2));
    return result;
  }
  if (options.command === 'draft') {
    const result = await upsertUnreleasedDraft(github);
    console.log(`Upserted ${UNRELEASED_NAME} draft from ${result.previous || 'the start of history'}.`);
    return result;
  }
  if (options.command === 'publish') {
    const result = await publishTagRelease(github, {
      tag,
      deleteUnreleased: String(env.DELETE_UNRELEASED || 'true').toLowerCase() !== 'false',
    });
    console.log(`Published ${tag} (previous ${result.previous || 'none'}).`);
    return result;
  }
  throw new Error(`Unknown command ${JSON.stringify(options.command) || '(missing)'}`);
}

const invokedDirectly = process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url;
if (invokedDirectly) {
  try {
    await main();
  } catch (error) {
    console.error(error.message || error);
    process.exitCode = 1;
  }
}
