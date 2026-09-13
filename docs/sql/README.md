# SQL scripts

Authoritative SQL for stored procedures, FTS DDL, and other hand-written database objects that are **not** fully expressed as EF LINQ.

## Contributor convention

1. **Large procedure bodies and FTS/bootstrap DDL live here first** (`docs/sql/*.sql`) so they are easy to review and re-run in SSMS.
2. **EF migrations apply the same SQL** (embedded in the migration for deploy reliability). When you change a proc:
   - Update the `.sql` file in this folder.
   - Update the matching migration (or add a new idempotent `CREATE OR ALTER` migration) so production still receives the change.
3. Prefer `CREATE OR ALTER PROCEDURE` and idempotent `IF NOT EXISTS` DDL.
4. FTS catalog/index creation must use `suppressTransaction: true` in migrations (SQL Server limitation).

## Inventory (selected)

| File | Purpose |
| --- | --- |
| `006-modern-forum-read-path.sql` | Modern forum read procs + indexes |
| `007-forum-search.sql` / related | Forum FTS |
| `008-news-full-text-search.sql` | `dbo.NEWS_T_SearchPublished` proc body (migration `20260729000000_AddNewsFullTextSearch`) |
| `009-photo-dimension-inventory.sql` | Read-only coverage of `PIC_WIDTH`/`PIC_HEIGHT` for public photos (issue #435) |
| `010-search-document-full-text-search.sql` | `dbo.SearchDocument_Search` proc body — unified whole-site search (migrations `20260804113500_AddSearchDocumentFullTextSearch`, `20260824120000_AddSearchDocumentSearchSourceKey`, `20260827143000_CapSearchDocumentSearchMatches`, `20260908140000_CapTypedSearchAfterContentTypeFilter`) |
| `011-modern-forum-archive-author-summary.sql` | Materialized visible-post count and latest identity for archive-author pages, maintained by a trigger on `ModernForumPost` |

Do not put connection strings or secrets in these files.

### Photo dimension inventory (#435)

```powershell
# Formatted tool report (preferred)
dotnet run --project src/QueenZone.Tools -- photo-dim-inventory --connection-string $env:ConnectionStrings__QueenZoneLegacy

# Or run the SQL script in SSMS / sqlcmd against the legacy database
# docs/sql/009-photo-dimension-inventory.sql
```

Post results as a comment on GitHub issue #435 for filter authors (#437).

### Backfill zero original dimensions (#438)

Dry-run first (default). Prefer blob storage connection when applying:

```powershell
$env:BWS_ACCESS_TOKEN = [Environment]::GetEnvironmentVariable("BWS_ACCESS_TOKEN", "User")
# load ConnectionStrings__QueenZoneLegacy (+ optional ConnectionStrings__BlobStorage) from bws — never print values
dotnet run --project src/QueenZone.Tools -- backfill-photo-dimensions --limit 20
dotnet run --project src/QueenZone.Tools -- backfill-photo-dimensions --limit 20 --apply
dotnet run --project src/QueenZone.Tools -- photo-dim-inventory
```

See `docs/agent-bitwarden-secrets.md` for secret loading. Default targets `DISPLAY = 1` rows with width or height zero.
