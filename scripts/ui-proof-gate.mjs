import { checkPullRequestVerification } from './check-pr-verification.mjs';

export async function checkEvidenceGate({ github, context, core, root, producerResult }) {
  if (['failure', 'cancelled'].includes(producerResult)) {
    core.setFailed('Web browser tests did not pass.');
    return;
  }
  let pulls = [];
  if (context.payload.pull_request) {
    const { data } = await github.rest.pulls.get({ ...context.repo, pull_number: context.payload.pull_request.number });
    if (data.head.sha !== context.payload.pull_request.head.sha) {
      core.setFailed('PR head changed; this evidence check is stale.');
      return;
    }
    pulls = [data];
  } else if (context.payload.merge_group) {
    const group = context.payload.merge_group;
    const commits = await github.paginate(github.rest.repos.compareCommitsWithBasehead,
      { ...context.repo, basehead: `${group.base_sha}...${group.head_sha}`, per_page: 100 }, response => response.data.commits);
    const shas = new Set(commits.map(commit => commit.sha));
    const open = await github.paginate(github.rest.pulls.list, { ...context.repo, state: 'open', base: 'main', per_page: 100 });
    pulls = open.filter(pull => shas.has(pull.head.sha));
    if (!pulls.length) { core.setFailed('Cannot resolve merge-group PR heads; refusing to bypass evidence.'); return; }
  } else {
    core.info('Manual CI run: no PR to verify.');
    return;
  }
  for (const pull of pulls) {
    await checkPullRequestVerification({ github, context: { ...context, payload: { pull_request: pull } }, core, root });
  }
}
