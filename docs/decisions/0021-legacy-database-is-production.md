# ADR 0021: The Legacy Database Is The Production Database

## Status

Accepted. Supersedes ADR 0004.

## Context

ADR 0004 framed the legacy Azure SQL database (`MAIN2_DB` / `queenzone-db`) as an
**import source and historical reference**: something QueenZone Modern reads
from and imports out of, on the assumption that its long-term role was to feed
a separate, cleaner destination. `docs/architecture/database-evolution-plan.md`
"Migration Rules" made that assumption explicit — "store imported modern data
in separate tables **or a separate database**," "prefer read-only credentials
for the legacy database," "do not mutate legacy data."

In practice this stopped being true some time ago. ADR 0006 documents that
modern write paths (admin news, `NewsAuditLog`, private messaging, member
accounts, member follows, forum topic watches, help requests, news discovery,
news-agent guidance and run leases, queen history) are already EF Core tables
living inside this **same** physical Azure SQL database, not a separate one.
There is no destination database anywhere in this codebase's infrastructure —
one Azure SQL Database (Standard S0 tier, 10 GB / 10 DTU, `queenzone-db`) is the entire production
data store, and it has been taking live writes from the running application
for some time.

Treating it as a frozen, read-only import source is therefore inaccurate and
increasingly gets in the way: it discourages ordinary schema maintenance (for
example, dropping the long-dead `Q_FORUM_TOPIC_V` view that references a
table which no longer exists — see `scripts/Sync-LegacyDbToSqlExpress.ps1`)
and implies a two-database future that was never built and is not planned.

## Decision

There is one production database. It is not an import source for a future
separate system, and it never will be — retire that framing.

- `queenzone-db` (Azure SQL Database) is the permanent production data store
  for both legacy and modern tables. There is no plan, now or later, to
  migrate its data into a separate destination database.
- The schema — legacy tables included — may be altered like any other
  production schema: through reviewed, repeatable migrations (EF Core
  migrations for EF-managed tables; reviewed idempotent SQL scripts under
  `docs/sql/` for the rest, per ADR 0006's contributor rules). "It's a legacy
  table" is not by itself a reason to leave a schema problem in place; it also
  isn't a license for unreviewed or unexplained changes — the same review bar
  applies as anywhere else.
- New write paths still default to EF Core against deliberately designed
  tables (ADR 0006 is unchanged by this decision) — this ADR removes the
  *reason* that was previously given for treating legacy tables as
  off-limits, it does not mandate rewriting them.
- Ordinary data-safety practice still applies: back up before destructive
  schema changes, avoid changes that break in-flight application code, and
  keep migrations reversible where practical. That is normal production
  discipline, not a holdover of the retired import-source policy.

### What does not change

- ADR 0001 (archive-first public rollout, public pages read-only *for site
  visitors*) is untouched — it is about which features the public site
  exposes, not about who may run DDL against the database.
- ADR 0006 (EF Core as the data-access library, the write-path matrix,
  contributor rules) is untouched and remains the governing document for how
  writes are implemented.
- The nightly SQL Express mirror (issue #1501 and its sync script) still
  needs to exist and still needs its own source read to stay non-destructive
  during the sync window — that constraint comes from not wanting a flaky
  sqlpackage run to disrupt a live production database mid-request, not from
  the retired "the source must stay frozen" framing.

## Consequences

Benefits:

- Documentation matches reality: contributors stop being told to treat a
  live, actively-written database as a frozen reference.
- Legacy schema cleanup (dead views, unused objects) becomes ordinary
  maintenance instead of an exception nobody wants to be first to make.
- Removes a source of confusion in evaluations like issue #1501, where
  "the legacy DB is read-only" was previously cited as a hard constraint it
  never actually was.

Tradeoffs:

- Contributors need the same production-change discipline (review, backups,
  reversibility) for legacy tables that already applies to modern ones —
  there is no separate database anymore to make a low-stakes rehearsal copy
  against.
- Any doc or comment that still says "import source," "read-only for the
  legacy database," or "store modern data in a separate database" is stale
  and should be corrected as it's found; this ADR does not attempt to sweep
  every reference at once.

## Superseded guidance

- ADR 0004 "Treat Legacy Schema As Import Source" — superseded by this ADR.
  Its non-DB-ownership content (forum public reads defaulting to
  `ModernForum*` tables, other content reading legacy tables until proven
  otherwise) is still accurate operational description and is restated here
  informally, but the "import source" framing itself is retired.
- `docs/architecture/database-evolution-plan.md` "Migration Rules": "prefer
  read-only credentials for the legacy database," "do not mutate legacy
  data," and "store imported modern data in ... a separate database" are
  superseded by this ADR's Decision section.
- `AGENTS.md` "Migration Principles": "treat the legacy database as an import
  source and historical reference" is superseded by this ADR.
