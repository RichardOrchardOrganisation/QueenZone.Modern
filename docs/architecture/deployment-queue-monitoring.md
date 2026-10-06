# Dev deployment queue monitoring

Gardener's hourly `23 * * * *` slot inspects only the `deploy-dev.yml` main-ref
queue using GitHub Actions run, job and pending-deployment metadata. Its weekly
Gardener slot and manual inputs retain their previous behavior. Scheduled
Actions can start late; this is an hourly opportunity, not a delivery SLA.

A run is reported when **every unfinished job is queued and unassigned**, no
job is in progress, no environment approval is pending, and the youngest queued
job has waited at least **60 minutes**. Queue age uses the queued job's
`started_at` metadata (populated even before runner assignment in historical
run 37320201364), bounded by run creation. If that timestamp is absent, the
run's latest update is a conservative fallback. Invalid or future timestamps
are reported as unknown, not stalled. Old run creation alone does not make a
newly queued phase stale.

Active jobs, assigned queued jobs, environment approvals, unknown waiting
states and run-status lag are separate classifications. A pending run without
jobs behind an older unfinished dev run is a **possible concurrency wait**:
GitHub does not expose the actual run concurrency group, so metadata cannot
prove that relationship. It is never counted as a second blocker. Production
tags and approval gates are outside this detector's scope.

Every inspection writes a job summary. An aged unassigned job also produces a
warning annotation and a filing plan with run links; its cause remains
unproven. No credential logs, secret values, runner names, reviewers or arbitrary
run titles are collected into the report. Pagination, schema, HTTP or changed
attempt errors fail inspection rather than reporting a clean queue. Each list
is bounded to its first 100-item page, with at most 20 distinct unfinished
runs; any truncation is an explicit acceptance gap.

The collector uses the existing issue filer, the stable
`deployment-queue:deploy-dev:main:unassigned` marker, existing cooldown and
deduplication, and the shared telemetry cap of three issues per day, with at
most one new issue per poll. It reuses Gardener's current token permissions,
labels and issue-filer concurrency lock. No credentials or permissions are
added. Scheduled issue writes remain **dry-run unless the existing**
`TELEMETRY_TRIAGE_FILE_ISSUES` **variable is exactly `true`**. This change does
not modify that variable. If filing is capped, ignored or disabled, the job
summary and warning still show the blocker; a maintainer must review the
filing plan. It does not guarantee an external notification in dry-run mode.

## Operator response

Open the linked run and freshly inspect jobs, assignments, pending approvals
and runner capacity. A queued unassigned job is evidence of waiting, not proof
of a runner outage or GitHub fault. Normal cancellation can leave a job whose
`always()` condition stays true: historical run 37320201364 required a separately
authorized force cancellation. Any cancellation remains an operator decision
after fresh checks. The detector never cancels, force-cancels, reruns, dispatches,
restarts an app, writes a database or changes deployment concurrency.

## Verification

Offline Node tests reproduce the historical holder shape, recent queue phases,
approval/active/assigned exclusions, possible concurrency waits, API failure and
truncation, stale attempts, metadata privacy, dry-run behavior and existing
filing caps/deduplication. Workflow syntax is checked with actionlint. No live
DB tests, Bitwarden requests, workflow dispatches or persistent app hosts are
needed. Runtime acceptance remains the first ordinary hourly inspection after
publication; offline fixtures cannot prove future runner or API availability.
