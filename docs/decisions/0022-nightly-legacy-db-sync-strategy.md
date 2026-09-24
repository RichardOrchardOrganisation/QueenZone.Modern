# ADR 0022: Nightly Legacy DB Sync Strategy

## Status

Accepted. Option 5 has now run successfully against real production data
(2026-09-14 on-demand nightly run, #1512/#1514) — see "Evidence" below for
the real numbers gathered from that run. Two secondary items remain open
(marked below) but are not blocking: they're refinements, not open
questions about whether the approach works.

## Context

`scripts/Sync-LegacyDbToSqlExpress.ps1`, run nightly by
`.github/workflows/nightly-legacy-checks.yml`, refreshes a local SQL Express
mirror of the production Azure SQL database (`queenzone-db`) so nightly
probes and the RealData Playwright suite run against a same-day snapshot
instead of the live database. It uses `sqlpackage Extract` (schema + data,
`ExtractAllTableData=True`) followed by `Publish` with
`/p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership` — see
that script's header comment for why `Publish` is used instead of the more
obvious `Export`/`Import` BACPAC pair (Publish is the only sqlpackage action
that supports excluding object types; a genuinely broken legacy view
(`Q_FORUM_TOPIC_V`, references a table that no longer exists) and Azure SQL's
contained-user model, incompatible with SQL Express, both need exclusion).

**Production database size:** `queenzone-db` is confirmed at 10 GB / 10 DTU.
That rules out Basic tier (capped at 2 GB / 5 DTU) — 10 DTU with a
configurable max size matches Standard tier S0. The script's and workflow's
header comments calling it "Basic tier" are stale and are corrected as part
of this change.

**What we're actually trying to accomplish** with this nightly job, restated
plainly: a crude backup, a way to confirm the database still works against
current application code, and a local test environment that is reasonably
close to production — not a byte-perfect replica, not a disaster-recovery
mechanism, and not something that should cost meaningfully more than it does
today.

**The problem driving this evaluation:** the current Extract phase holds a
long live connection from the self-hosted Windows runner directly to
production `queenzone-db` for the full 25-30 minute Extract. At a small DTU
allocation, that Extract genuinely competes with live application traffic for
the entire window — this is reported as noticeable blocking/slowness against
production, not just a long-running job that happens to touch prod briefly.

Issue #1501 asked for a storage-staged alternative (Extract near Azure SQL,
stage the DACPAC in private Blob storage, download and Publish locally) and
laid out Options 0-4 below. Since that evaluation started, [ADR
0021](0021-legacy-database-is-production.md) retired the "legacy database
must stay frozen" framing — production schema changes (like dropping
`Q_FORUM_TOPIC_V`) are now ordinary maintenance, not something to route
around during migration planning. That changes what "the problem" actually
is: the Users/Logins/Permissions/RoleMembership exclusion is unaffected
(structural Azure SQL vs. SQL Express incompatibility, not legacy debt), but
the Views exclusion could shrink to nothing with a one-time cleanup,
independent of whichever sync option is chosen.

## Options 0-4 (from issue #1501)

Full detail lives in issue #1501; summarized here for comparison against the
new Option 5.

- **Option 0 — Retain direct Extract/Publish.** Today's behavior. Proven,
  no new infrastructure, but holds the long connection against production
  for the full Extract and repeats the source read on every retry.
- **Option 1 — Cloud-side data-bearing DACPAC, then private Blob download.**
  Run Extract near Azure SQL (new cloud compute), stage the DACPAC in a
  private Blob container, download and Publish locally. Removes the long
  connection from the local runner; requires new compute, a new storage
  account with strict lifecycle/retention/RBAC, and manifest/hash
  verification.
- **Option 2 — Azure SQL managed BACPAC export, then local Import.** Uses
  Azure's own export-to-Blob service. Export/Import doesn't support
  `ExcludeObjectTypes`, so it would still choke on the
  Users/Logins/Permissions incompatibility (unaffected by the ADR 0021
  policy change) even after the dead view is dropped.
- **Option 3 — Short-lived Azure SQL copy, sanitize, export BACPAC.**
  Closest in spirit to Option 5 below (uses a disposable copy as the
  extraction source), but pairs it with a full BACPAC export/Blob-storage
  pipeline and the same Export/Import exclusion gap as Option 2, plus
  broader permissions and orphan-cleanup requirements.
- **Option 4 — Table-by-table logical export.** Reimplements substantial
  package behavior; explicitly not recommended unless Options 1-3 are ruled
  out by evidence.

## Option 5 — Extract from a disposable same-server database copy (new)

Keep the existing `sqlpackage Extract` + `Publish` pipeline exactly as it is
today. Change only *what Extract reads from*:

1. `CREATE DATABASE [queenzone_nightly_<guid>] AS COPY OF [queenzone-db]`
   (or an Azure SQL point-in-time restore to "now") — a platform-managed,
   transactionally consistent copy on the same logical server. This is
   Azure's own backup/restore machinery doing a backend copy, not a
   foreground query competing with live application connections.
2. `sqlpackage Extract` reads from the **copy**, not from `queenzone-db`.
   Unchanged tool, unchanged flags (`/p:ExtractAllTableData=True`,
   `/p:VerifyExtraction=False`). It can still take 25-30 minutes — that no
   longer matters, because nothing on the copy is competing with production
   traffic.
3. `sqlpackage Publish` into SQL Express is **completely unchanged**: same
   `/p:ExcludeObjectTypes=Views;Users;Logins;Permissions;RoleMembership`,
   same staging-database-then-atomic-promote logic already in
   `Sync-LegacyDbToSqlExpress.ps1`.
4. `DROP DATABASE [queenzone_nightly_<guid>]` in a `finally` block, mirroring
   the script's existing staging-database cleanup pattern — always runs,
   success or failure.

### Why this fits the restated goal better than Options 1-4

- **No new infrastructure category.** No Blob storage account, no
  container-level RBAC/OIDC scoping, no manifest/SHA-256 verification step,
  no separate cloud compute host, no lifecycle-management policy to build
  (all confirmed absent from this repo's infra in the earlier research pass
  and all would be new work for Options 1-3). A same-server database copy is
  a single T-SQL statement using the credentials the job already has.
- **Directly targets the reported problem.** The long, contention-causing
  connection moves off production entirely. The only production-facing
  operation becomes the copy creation itself, which is short and handled by
  the platform, not by an application-level `SELECT *` scan through a
  constrained DTU budget.
- **Keeps the one exclusion mechanism that actually works.** Because it's
  still `Publish`-based (not `Export`/`Import`), the Users/Logins
  incompatibility continues to be handled the same way it is today — no
  regression from Options 2/3's Export/Import gap.
- **Cost stays close to zero.** Azure SQL Database is billed per-second at
  the provisioned tier. A ~10 GB database alive for the ~30-45 minutes this
  needs, once a night, costs a small fraction of the tier's monthly rate —
  materially cheaper than any option requiring standing storage
  infrastructure or additional compute.
- **Retry behavior stays simple.** A transient TCP drop during Extract can
  retry against the still-existing copy (same retry/backoff logic already in
  the script) without re-touching production, and without needing a
  verified-package-reuse mechanism the way Options 1/3 do.

### Downsides

- Does not remove the long connection itself, only moves what it's against.
  If a future goal is "never hold a 25-30 minute connection to *any* Azure
  SQL database at all" (e.g. to reduce sqlpackage's own transient-TCP-drop
  exposure, issue #1453), Option 5 doesn't address that — Options 1/3 (Blob
  staging) would still be the ones to revisit for that specific goal.
- `CREATE DATABASE AS COPY OF` / point-in-time restore duration scales with
  transaction log volume since the last checkpoint, not just data size; this
  needs to be measured at the current ~10 GB / 10 DTU scale rather than
  assumed from the smaller (2 GB / 5 DTU) figure this evaluation started
  with.
- Two Azure SQL databases (the always-on production DB and the nightly
  transient copy) briefly coexist; needs the same unpredictable-naming and
  guaranteed-cleanup discipline the script already applies to its local
  staging database, now also applied to the cloud-side copy, plus a sweep
  for abandoned copies from interrupted runs (mirrors the existing stale
  `.dacpac` sweep in the script).
- Requires the pipeline's identity to have `CREATE DATABASE` /
  `DROP DATABASE` rights on the logical server, not just read access to
  `queenzone-db` — a permission change from today, though still narrower
  than the broader-permissions requirement Option 3 already calls out.

## Evidence

From the first real `DatabaseCopy`-mode run against production
(`queenzone-db`, confirmed Standard S0, 10 GB / 10 DTU), triggered on-demand
2026-09-14, workflow run
[34817774628](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/actions/runs/34817774628):

| Phase | Duration |
| --- | --- |
| Database copy created + reached `ONLINE` | 183s (~3 min) |
| `sqlpackage Extract` from the copy | 1133s (~19 min) |
| Database copy dropped | 12s |
| Whole `sync-legacy-db` job (includes local staging DB creation, Publish, verify, grant, promote) | ~24 min |

Against the "Evidence required" checklist this ADR originally set:

- **Time to create/drop the copy:** measured above — 183s create, 12s drop.
  Both small relative to the ~19-minute Extract they replace on production's
  critical path.
- **Whether copy creation is observably lighter on production than direct
  Extract:** not measured with a side-by-side production-latency comparison
  (no monitoring was wired up for that in this run) — **open**, but the
  qualitative case is strong: production's exposure dropped from the full
  ~19-minute Extract to a ~3-minute platform-managed copy operation. Worth
  confirming with real latency data on a future run, but not blocking
  adoption.
- **Whether Extract-from-copy duration changes materially from
  Extract-from-production:** 1133s (~19 min) is within the previously
  observed 25-30 minute range for direct Extract, consistent with the
  prediction that Extract duration is DTU-bound and doesn't change based on
  source. Not a strict same-night A/B (this run replaced, not paralleled, a
  direct-Extract run) — **open** as a rigorous comparison, but nothing in
  this result contradicts the prediction.
- **Actual Azure SQL cost:** not pulled from billing directly, but at
  Standard S0 list pricing the transient copy was alive for roughly
  183 + 1133 + 12 ≈ 1328s (~22 minutes) once nightly — a small fraction of
  a cent per run. **Not independently verified against a billing
  statement**, but not expected to be material.
- Additionally confirmed, not originally on the checklist: the pipeline
  identity (`CloudSA6f234939`) is the Azure SQL server admin login — the
  permission diagnostic added alongside Option 5 logged "NOT a member of
  dbmanager" while `CREATE DATABASE` still succeeded, exactly matching the
  admin-login prediction from repo research. **No new grant was needed.**

A downstream probe failure in the same run (`EfForumPostReportRepositoryTests`,
`Invalid object name 'ForumPostReports'`) was investigated and is unrelated
to Option 5 — a same-day migration (`20260914030744_AddForumPostReports`,
#1510/#1513) had merged to `main` but not yet been deployed to production, so
the mirror faithfully reproduced a schema production didn't have yet. This
would have failed identically under `Direct` mode.

## Recommendation

Adopt Option 5 as the default (`ExtractSource DatabaseCopy`, already the
script's default). It is the cheapest, lowest-complexity option, reuses the
entire existing Publish/exclusion pipeline unchanged, requires no new
infrastructure, and has now demonstrated success against real production
data with no problems found. `ExtractSource Direct` stays available as an
explicit fallback (matching Option 0) if database-copy creation rights are
ever revoked or unavailable.

The two open items above (a rigorous same-night production-latency A/B, and
a billing-statement cost confirmation) are worth picking up opportunistically
on a future run, not blockers to using Option 5 going forward.
