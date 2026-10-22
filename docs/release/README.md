# Production release runbook

This is the single source of truth for shipping QueenZone to production. `AGENTS.md`, `README.md`, [`testing-policy.md`](../architecture/testing-policy.md) and [`github-environments.md`](../architecture/github-environments.md) all point here.

`deploy.yml` enforces the three tip gates in `scripts/release/check-gates.mjs`. The job is not a pull-request check. It fails closed, with no polling and no sleeps. On failure it prints the exact `gh workflow run` commands below.

## Scope

Diff `main`'s tip against the live production tag and list the pull requests going out:

```bash
git fetch origin main --tags
git log --oneline "$(git describe --tags --abbrev=0 --match 'v*')..origin/main"
gh pr list --search "$(git describe --tags --abbrev=0 --match 'v*')..origin/main" --state merged
```

Call out mobile changes that need a native store build (not an OTA). `src/QueenZone.Mobile` changes that affect generated Android/iOS projects still need Play / TestFlight; a website-only tag does not ship a store build.

A docs-, mobile- or workflow-only range does not run migrate or deploy. `check-gates` still runs, sees `changes.code=false` and `changes.migrations=false`, and succeeds without looking up the three workflows. Missing `changes` outputs fail closed.

## Three tip gates

Freeze `main`. Wait for all three gates to go green on the **exact tip SHA**. There is no ancestor rule and no waive. If a gate is red, stop and tell Richard.

The three gates:

1. **Nightly RealData** — `.github/workflows/nightly-legacy-checks.yml`. All jobs. A dispatch narrowed with `skip_sync` or `category_filter` does not count.
2. **DEV member auth** — `.github/workflows/dev-member-auth-e2e.yml` against `https://dev.queenzone.org`.
3. **DEV journey** — `.github/workflows/dev-journey-e2e.yml` against `https://dev.queenzone.org` (issue #2170).

DEV member-auth and DEV journey keep their 07:00 UTC crons and also run through `on: workflow_run` after a successful `deploy-dev`. Re-dispatch on every new tip if you cannot wait for that path:

```bash
gh workflow run nightly-legacy-checks.yml --ref main
gh workflow run dev-member-auth-e2e.yml --ref main
gh workflow run dev-journey-e2e.yml --ref main
```

DEV artifact reuse requires the artifact's commit SHA to equal the deployed main SHA. A PR and squash merge can have the same source tree but different embedded build stamps. In that case DEV publishes a fresh package from the checked-out main commit; it does not relabel a PR package or reinterpret an old proof. The exact-SHA release gate stays unchanged. After this policy changes, promotion still needs a fresh DEV deployment and new full gate runs; failed historical evidence cannot be reused.

### Explicit DEV redeploy for a non-application tip

An ordinary manual DEV dispatch still skips docs-, mobile-, and workflow-only tips. To obtain real exact-commit DEV evidence for such a release candidate, freeze main and deliberately dispatch:

```bash
gh workflow run deploy-dev.yml --ref main -f force_redeploy=true -f expected_main_sha=<full-lowercase-main-sha>
```

The guard runs before Azure login and requires the supplied SHA to match the workflow run, checkout, and current remote main. A missing, malformed, or stale SHA fails closed. The explicit option overrides only the web deployment flag; migration and mobile classification remain unchanged. The existing exact-commit artifact requirement and fresh-publish fallback stamp remain in force. It never relabels an old package or old proof. Main can move after validation: keep it frozen through deployment and proof generation, and recheck the served stamp and run SHA before promotion.

After a successful manual DEV deployment and its stamp/health checks, explicitly dispatch all three full tip gates using the commands above. The existing browser workflow_run triggers accept push-triggered DEV runs only; manual DEV deployment does not automatically run those suites. Do not narrow Nightly with skip_sync or category_filter. All proof runs must target the same main SHA. This DEV option does not authorize a production release or bypass any production proof check.

DEV proof jobs fetch full history to resolve the served stamp unambiguously to a commit in the workflow run tip’s history. If DEV lags that tip, the artifact credits only the served commit and the log and step summary say `no tip proof`; a green proof job alone cannot authorize release. Missing, ambiguous, non-commit or unrelated stamps fail closed. A deployment that races ahead of the fixed run tip also fails closed and needs a new run. The before/after stamp comparison still rejects a build change during the suite. These reads do not establish that no transient deployment occurred between them; freeze DEV and main through release verification.

Each gate uploads a `gate-proof` artifact `{ tested_sha, full_suite }`. For the DEV gates, `tested_sha` is the live DEV `data-build-version` stamp read in the test job before and after the suite; those two reads must match, and an unknown stamp fails closed. `deploy.yml` requires `tested_sha` to equal the deploy SHA. Freshness is at most 72 hours, set by the repo variable `PROD_GATE_MAX_AGE_HOURS`.

`check-gates.mjs` finds runs with `actions/workflows/{file}/runs?head_sha=SHA&branch=main` and judges **only the newest** run for that SHA. An older green run cannot hide a newer red, missing, or unreadable one. Then it fetches `runs/{id}/jobs` and requires the `gate-proof` job itself to have succeeded on the latest attempt. A run whose jobs were all skipped still reports workflow success and does not count.

An older or cherry-picked SHA cannot be gated through `workflow_run`. Shipping one needs the override.

Local / dry-run:

```bash
node scripts/release/check-gates.mjs --sha <full-or-short-sha>
```

`workflow_dispatch` of `deploy.yml` with `gate_only=true` runs the same check and stops before migrate.

## Tag

Use `vYYYY.MM.DD.N` on that SHA as soon as all three gates are green:

```bash
git checkout main
git pull --ff-only origin main
git tag vYYYY.MM.DD.N <gated-sha>
git push origin vYYYY.MM.DD.N
```

Cherry-picked or older commits still need all three gates on that exact SHA.

## Release notes

Pushing a `v*` tag publishes a GitHub Release for that tag. Every merge to `main` refreshes one draft release named **Unreleased** (placeholder tag `unreleased`, `target_commitish: main`) covering main since the last prod tag. Notes are grouped by pull-request label; see [labels.md](labels.md). After generation, each pull-request line gets `(closes #N)` from GitHub's closing-issue links.

`.github/workflows/release-notes.yml` is non-blocking. It is not a required check and is not in any `needs` of `deploy.yml` or the tip gates. A red notes run does not delay production.

Link the release from the #2222 deploy log using the tag. The URL does not wait for the notes job:

`https://github.com/<repo>/releases/tag/<tag>`

Backfill a missing tag (acceptance: `v2026.10.22.1` since `v2026.10.15.1`):

```bash
gh workflow run release-notes.yml --ref main -f tag=v2026.10.22.1
```

## Deploy

The `v*` tag starts `.github/workflows/deploy.yml`. Watch that run to completion. The brief outage on in-place deploys is accepted.

`workflow_dispatch` must use a `v*` tag ref. Dispatching `main` no longer deploys. Tag pushes cannot be overridden; re-dispatch the tag with a reason instead.

Never use **Re-run failed jobs** on a Deploy run. That reuses a possibly stale `check-gates` result. Re-run the entire workflow so the tip-gate job runs again.

`gate_only` is a dry run: it runs `check-gates` and skips migrate, configure, deploy, and smoke.

## OpenTofu

If a merge triggers [`opentofu-apply.yml`](../architecture/opentofu-ci-and-runbooks.md), read the plan and summarise it for Richard before he approves. OpenTofu does not manage blob objects or SQL rows. See [`opentofu-contributor-runbook.md`](../architecture/opentofu-contributor-runbook.md) for `prevent_destroy` and rollback by resource class.

## Post-deploy health

After `post-deploy-smoke` is green:

1. Confirm `/health`, `/warmup`, and `data-build-version` on `/` at `https://www.queenzone.org`.
2. Check App Insights errors and latency for the new stamp.
3. Hit the specific routes the release changed.

`scripts/Smoke-LiveSite.ps1` / `scripts/Invoke-PostDeploySmoke.sh` are the cheap canaries. They do not replace the route check.

## Log it

Add a comment to #2222 with environment, tag, commit, PRs, who shipped it, the GitHub Release (`https://github.com/<repo>/releases/tag/<tag>`), and the health result. DEV deploys get logged too. The release URL is built from the tag, so it does not depend on the notes workflow having finished.

A gate override posts its own comment on #2222 **before** migrate. That job needs `issues: write`. If the comment fails, the deploy fails.

## Rollback

Do not dispatch an old tag. GitHub runs the `deploy.yml` from that ref, and tags from before the tip-gate merge have no `check-gates` job.

Preferred path — the previous SHA still has three fresh full-suite gates **and** that commit contains the gated `deploy.yml` that targets `prod-release-gated` / `prod-deploy-gated`:

1. Create a new `vYYYY.MM.DD.N` on that SHA and push. Do not reuse the old tag name.
2. Watch the new tag's Deploy run.

If that SHA has the gated workflow but the gates have expired, dispatch the **new** tag with `gate_override_reason` (at least 20 characters). Richard approves `prod-gate-override`. The reason is passed via `env`, written to the job summary, and commented on #2222 before migrate.

If the previous SHA is from before the tip-gate merge (#2292) or before the gated environment rename (#2294), tagging it again still runs the historical workflow against `prod-release` / `prod-deploy`. After Gilfoyle deletes those environments, that path fails at Azure login (proof target: dispatch `v2026.10.13.3`). That is the intended block. Do not recreate the old names. Ask Richard if a pre-gate SHA must be rebuilt onto a new tag that uses this workflow.

## Roles

Anyone following this runbook may tag. Workflow-file pull requests are merged by Richard.

Richard is the required reviewer for a gate override and for the gated production environments. He approves `prod-gate-override`, `prod-release-gated`, and `prod-deploy-gated`. **Prevent self-review** is off: he is the only reviewer and must be able to approve his own production releases. The same reviewer and Prevent self-review off apply only to `prod-release-gated`, `prod-deploy-gated`, and `prod-gate-override`. `prod-data-read` has no required reviewer — only a custom deployment branch policy allowing `main`, so scheduled sync jobs do not wait for approval.

Those three environments already exist in Settings (Gilfoyle). Agents cannot create them. Do not add a GitHub App protection rule.

| Setting | `prod-gate-override` | `prod-release-gated` / `prod-deploy-gated` |
| --- | --- | --- |
| Required reviewers | `richardorchard` | `richardorchard` |
| Prevent self-review | Off | Off |
| Wait timer | None | None |
| Deployment branches | Custom; tags `v*` only (not `main`) | Custom; tags `v*` only (not `main`) |
| Environment secrets | None | None (variables only; OIDC on the gated pair) |

Ungated `prod-release` and `prod-deploy` stay in Settings only until Gilfoyle deletes them after #2294 merges. Old tags keep those names and then fail at Azure login. This rename **blocks the next production release** until that cutover is done.

## Override path

`gate_override_reason` is an optional `workflow_dispatch` input, allowed on `v*` tag refs only. When it is set:

1. `gate-override-guard` checks the ref and that the reason is at least 20 characters.
2. `gate-override` runs on `prod-gate-override`. Richard is the required reviewer.
3. The reason is written to the job summary and commented on #2222.
4. Migrate starts only after that comment succeeds.

Tag pushes never take this path.

## Prepared production packages

Production prepares a fresh package from the exact tagged commit after the initial proof gates. It does not reuse PR web-publish artifacts. Preparation has no production environment or OIDC permissions. The archive and manifest are retained as release-package-RUN-ATTEMPT for seven days, outside the web-publish cleanup policy. Every production job must acquire the immutable artifact ID, verify its SHA-256 and release/run/attempt manifest, and recheck normal-path proofs before fetching production credentials. A missing or corrupt package fails closed; never fall back to another commit. Emergency overrides retain their existing separate owner-reviewed path.

The production concurrency group is shared across tags with cancellation disabled for running releases. A newer pending run can supersede another pending run under GitHub concurrency semantics; this is not FIFO scheduling. Re-run the whole workflow after a failure, never only failed jobs, so preparation and gates run on the new attempt.

Repository cleanup cannot delete release-package artifacts through the scheduled web-publish prefix filter, superseded-PR cleanup, or CI ephemeral cleanup. GitHub's seven-day retention and administrator deletion remain possible: package acquisition fails before that job's own mutation. Settings, migrations and application deployment remain separate operations, not one atomic transaction; earlier successful operations are not automatically undone on a later failure.

This workflow change does not remove required environment reviewers or change identity permissions. Removing routine approval prompts requires a separately reviewed security configuration change. Workflow YAML by itself is not a tamper-resistant boundary against an actor who can replace that YAML or change repository protections.
