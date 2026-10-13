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
    const numbers = new Set();
    const queuedNumber = group.head_ref?.match(/^refs\/heads\/gh-readonly-queue\/main\/pr-(\d+)-/)?.[1];
    for (const commit of commits) {
      // Squash queue commits do not contain the original PR head in ancestry.
      const associated = await github.paginate(github.rest.repos.listPullRequestsAssociatedWithCommit,
        { ...context.repo, commit_sha: commit.sha, per_page: 100 });
      const candidates = associated.filter(pull => pull.state === 'open' && pull.base.ref === 'main');
      if (candidates.length === 1) numbers.add(candidates[0].number);
      // GitHub may not index the temporary commit yet. The queue ref identifies
      // its PR unambiguously only when the range contains one squash commit.
      else if (!candidates.length && commits.length === 1 && commit.sha === group.head_sha && queuedNumber) numbers.add(Number(queuedNumber));
      else { core.setFailed('Cannot resolve merge-group PRs; refusing to bypass evidence.'); return; }
    }
    if (!numbers.size) { core.setFailed('Cannot resolve merge-group PRs; refusing to bypass evidence.'); return; }
    for (const number of numbers) {
      const { data } = await github.rest.pulls.get({ ...context.repo, pull_number: number });
      if (data.state !== 'open' || data.base.ref !== 'main') {
        core.setFailed('Merge-group PR is no longer open against main.'); return;
      }
      pulls.push(data);
    }
  } else {
    core.info('Manual CI run: no PR to verify.');
    return;
  }
  for (const pull of pulls) {
    await checkPullRequestVerification({ github, context: { repo: context.repo, payload: { pull_request: pull } }, core, root });
  }
}
