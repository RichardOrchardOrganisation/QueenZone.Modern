import { discoverEvidence } from './ui-proof-evidence.mjs';

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
  const tokens = new Set(String(text || '').match(/[A-Za-z0-9][A-Za-z0-9._-]*/g) || []);
  return [...ids].filter(id => tokens.has(id)).sort((left, right) => left.localeCompare(right, "en"));
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
  for (const line of visibleMarkdown(body).split('\n')) {
    let text = line.trim();
    if (text.startsWith('-')) text = text.slice(1).trimStart();
    const prefix = 'verification-skip-reason:';
    if (text.toLowerCase().startsWith(prefix)) return text.slice(prefix.length).trim();
  }
  return '';
}

export function evaluateProof({ body = '', labels = [], requirements = [], features = [], evidence = [], headSha = '' }) {
  const uiFiles = [...new Set(requirements.map(item => item.file))];
  const ui = uiFiles.length > 0;
  const names = labels.map(label => typeof label === 'string' ? label : label.name);
  if (names.includes('no-ui-verification')) {
    const skipReason = verificationSkipReason(body);
    return skipReason ? { ok: true, ui, uiFiles, skipped: true, skipReason }
      : { ok: false, ui, uiFiles, error: 'no-ui-verification is set but the PR body has no Verification-skip-reason: line.' };
  }
  if (!ui) return { ok: true, ui, uiFiles };
  const section = extractVerificationSection(visibleMarkdown(body));
  if (!section) return { ok: false, ui, uiFiles, error: 'UI PR is missing a ## Verification section.' };
  const ids = findFeatureIds(section, new Set(features.map(feature => feature.id)));
  if (!ids.length) return { ok: false, ui, uiFiles, error: '## Verification must name at least one feature-map id.' };
  for (const requirement of requirements) {
    if (!requirement.ids.some(id => ids.includes(id))) {
      return { ok: false, ui, uiFiles, ids, error: `No affected feature declared for ${requirement.file}.` };
    }
  }
  const missing = missingProof(ids, features, evidence, headSha);
  return missing.length ? { ok: false, ui, uiFiles, ids,
    error: `Uploaded passing proof missing for current head: ${missing.join(', ')}. Local paths, arbitrary links and Not verified do not satisfy this gate.` }
    : { ok: true, ui, uiFiles, ids };
}

function missingProof(ids, features, evidence, headSha) {
  const missing = [];
  for (const id of ids) {
    const feature = features.find(item => item.id === id);
    if (!feature.platforms?.length) { missing.push(`${id}/unconfigured-platform`); continue; }
    for (const platform of feature.platforms) {
      if (!evidence.some(item => item.feature === id && item.platform === platform && item.headSha === headSha
        && item.url && (feature.fixtures || []).every(fixture => item.fixtures?.includes(fixture)))) missing.push(`${id}/${platform}`);
    }
  }
  return missing;
}

export async function checkProof({ github, context, core, root, loadPolicy, discover = discoverEvidence }) {
  const pull = context.payload.pull_request;
  if (!pull) { core.info('No pull request payload; skipping verification check.'); return { ok: true, skipped: true }; }
  const files = await github.paginate(github.rest.pulls.listFiles, { ...context.repo, pull_number: pull.number, per_page: 100 });
  const input = { ...loadPolicy(files.map(file => file.filename)), body: pull.body || '', labels: pull.labels || [], headSha: pull.head?.sha || '' };
  let result = evaluateProof(input);
  const evidence = !result.ok && result.ids?.length ? await discover({ github, repo: context.repo, headSha: input.headSha, root }) : [];
  if (evidence.length) result = evaluateProof({ ...input, evidence });
  if (!result.ok) core.setFailed(result.error);
  else core.info(result.skipped ? `Verification waived: ${result.skipReason}` : 'Uploaded verification evidence checked.');
  return { ...result, evidence };
}
