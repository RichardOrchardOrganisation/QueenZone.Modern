# ADR 0006: Hybrid EF Core For Admin Writes

## Status

Accepted.

Amended 2026-07-09: documented the full Dapper vs EF repository matrix, contributor rules, and a target direction to standardize on EF Core as the single data-access library while keeping stored procedures for hot read paths.

Amended 2026-07-09: migrated biography public reads to EF Core (`EfBiographyRepository`) while retaining the same legacy stored procedures.

Amended 2026-07-09: completed migration of remaining public-read repositories off Dapper onto EF Core (`SqlQuery` / `SqlQueryRaw` / EF-managed SQL Server proc calls). Removed the Dapper package reference from `QueenZone.Data`.

Amended 2026-07-27: register `IDbContextFactory<QueenZoneDbContext>` alongside scoped `QueenZoneDbContext` for SQL-backed mode so parallel admin/reporting reads can use independent contexts (issue #335).

Amended 2026-08-26: add modern `NewsAgentGuidanceRevisions` (issue #929) for admin-published NewsAgent editorial overlays. No seed; empty or missing published content is the compiled default.

## Context

ADR 0003 chose Dapper for initial legacy database access. Issue #5 added admin write workflows for `NEWS_T` and `NewsAuditLog` using hand-written SQL.

Admin writes benefit from typed entities, change tracking, and migrations. Public news reads still rely on legacy deduplication (`ROW_NUMBER()` over `NEWS_ID`) and remain a better fit for explicit SQL.

As more content areas landed, the hybrid split grew beyond news. Without a written matrix, new work can accidentally introduce a third SQL style (ad-hoc SQL in page models or tools) or pick Dapper/EF inconsistently.

## Decision

Use **EF Core as the single data-access library** in `QueenZone.Data`, while keeping stored procedures for hot or proven read paths:

### Current split

- **EF Core** for all SQL-backed repositories (admin/editorial writes, modern workflow tables, and public reads).
- Public and forum reads that already used stored procedures continue to invoke the **same** procs via EF (`Database.SqlQuery` / `SqlQueryRaw`) or the EF-managed SQL Server connection helper (`EfSql`) when output parameters are required.
- Complex projected SQL (for example public news latest-row CTEs) stays as explicit SQL, executed through EF rather than Dapper.
- Map legacy `NEWS_T` in EF as an existing table excluded from schema migrations where admin news needs it.
- Manage modern tables (`NewsAuditLog`, discovery, members, queen history, and similar) through EF migrations plus any required SQL bootstrap scripts.
- Keep **all** SQL Server access inside `QueenZone.Data`. Page models and tools must not open their own connections or invent a third SQL style (tools may construct `QueenZoneDbContext` + repositories).

`QueenZoneDbContext` is registered scoped. When SQL-backed, `IDbContextFactory<QueenZoneDbContext>` is also registered so admin/reporting code can create independent short-lived contexts for parallel reads (issue #335). Prefer the factory for direct EF work that must run concurrently; prefer independent DI scopes (as on the admin dashboard) when the work goes through scoped repositories. All SQL-backed repositories remain scoped. In-memory repositories are still used when no SQL connection string is configured.

### Access matrix (SQL-backed registrations)

| Content / concern | Interface | SQL implementation | Access style | Notes |
| --- | --- | --- | --- | --- |
| Public news archive | `INewsRepository` | `EfNewsRepository` | EF Core + SQL | Legacy `NEWS_T` latest-row projections via `PublishedNewsQuery` (extension point for future modern approved-news tables, issue #7) |
| Articles | `IArticlesRepository` | `EfArticlesRepository` | EF Core + SQL | Legacy reads |
| Biography | `IBiographyRepository` | `EfBiographyRepository` | EF Core + stored procedures / SQL | Reads: `Q_BIO_LIST_SP`, `Q_BIO_DISPLAY_SP`. Admin create/update mirrors `Q_BIO_UPDATE_SP` against `Q_BIO_T`. |
| Photography | `IPhotoRepository` | `EfPhotoRepository` | EF Core + targeted SQL (`PhotoSqlQueries`) | Counts/paging/neighbor navigation; legacy `Q_PIC_CAT_PAGE4_SP` avoided (full-category temp table) |
| Discography | `IDiscographyRepository` | `EfDiscographyRepository` | EF Core + stored procedures | `Q_ALBUM_*` procs |
| Fan performances | `IFanPerformanceRepository` / `IAdminFanPerformanceRepository` | `EfFanPerformanceRepository` / `EfAdminFanPerformanceRepository` | EF Core + stored procedures / EfSql | Public reads: `Q_STAGE_T_PAGE_SP` + count/detail SQL. Admin create/update/hide write existing `Q_STAGE_T` columns (photo-style; table stays outside EF schema ownership). |
| Legacy member lookup | `ILegacyMemberLookupRepository` | `EfMemberLookupRepository` | EF Core + SQL | Legacy `USERS_T` read |
| Forum (default) | `IForumRepository` | `ModernForumRepository` | EF Core + stored procedures | Modern `ModernForum*` tables; procs in `docs/sql/006-modern-forum-read-path.sql` |
| Forum (rollback) | `IForumRepository` | `LegacyForumRepository` | EF Core + SQL / stored procedures | Opt-out via `ForumData:UseModernForumReads = false` |
| Admin news writes | `IAdminNewsRepository` | `EfAdminNewsRepository` | EF Core (+ `FromSqlRaw` for legacy news projections) | Writes/migrations; hybrid SQL for `NEWS_T` reads; latest-row CTE from `PublishedNewsQuery` (same dedup expression as public, without published filter) |
| News audit | `INewsAuditRepository` | `EfNewsAuditRepository` | EF Core | Modern audit table |
| Member accounts | `IMemberAccountRepository` | `EfMemberAccountRepository` | EF Core | Modern tables |
| Private messaging | `IPrivateMessageRepository` | `EfPrivateMessageRepository` | EF Core | Modern `PrivateConversations` / `PrivateMessages` tables (MVP 1:1) |
| Member follows | `IMemberFollowRepository` | `EfMemberFollowRepository` | EF Core | Modern `MemberFollows` table; used by private-message privacy |
| Forum topic watches | `ITopicWatchRepository` | `EfTopicWatchRepository` | EF Core | Modern `MemberTopicWatches` table; deliberate Watch opt-in for forum reply pushes (#735) |
| Help requests | `IHelpRequestRepository` | `EfHelpRequestRepository` | EF Core | Modern `HelpRequests` table; public `/contact` form + admin inbox |
| News discovery / agent drafts | `INewsDiscoveryRepository` | `EfNewsDiscoveryRepository` | EF Core | Modern workflow tables |
| News agent editorial guidance | `INewsAgentGuidanceRepository` | `EfNewsAgentGuidanceRepository` | EF Core | Modern `NewsAgentGuidanceRevisions` table; one published overlay per type (`triage` / `draft`); empty published content means compiled default |
| News agent run leases | `INewsAgentRunLeaseService` | `EfNewsAgentRunLeaseService` | EF Core (+ SQL for lease upsert) | Modern lease table |
| Queen history / on-this-day | `IQueenHistoryRepository` | `EfQueenHistoryRepository` | EF Core | Modern table |

When `ConnectionStrings:QueenZoneLegacy` is empty, the matching `InMemory*` implementations are used instead (local/Testing).

### Contributor rules

1. **New write paths default to EF Core** against deliberately designed modern tables (or carefully mapped legacy tables when that is the accepted write target).
2. **Complex legacy or projected read shapes may keep explicit SQL/stored procedures** until a modern table or EF mapping is justified; invoke them through EF, not a second client library.
3. **Do not add a third ad-hoc SQL style** in Razor page models, minimal endpoints, or tools. New SQL belongs in `QueenZone.Data` repositories.
4. Prefer extending an existing repository over opening a one-off `SqlConnection` elsewhere.
5. **Do not reintroduce Dapper** (or another micro-ORM) for new code without a new ADR.
6. **Keep EF migrations and the model snapshot in sync.** Hand-written SQL migrations are allowed for careful idempotent DDL, but EF still requires migration designer metadata and `QueenZoneDbContextModelSnapshot` to match the runtime model. Run `dotnet ef migrations has-pending-model-changes --project src/QueenZone.Data/QueenZone.Data.csproj --startup-project src/QueenZone.Web/QueenZone.Web.csproj` before PRs that change `QueenZoneDbContext`, mapped entities, or migrations.
7. **Large stored procedure / FTS bodies:** keep a reviewable copy under `docs/sql/` (see `docs/sql/README.md`) and keep the applying EF migration in sync. Prefer `CREATE OR ALTER` for procs.

### Target direction (standardize on EF Core)

It is desirable to standardize on **EF Core as the single data-access library** for both old and new tables, without giving up stored procedures where they already win on shape or performance.

Important distinction:

- **Library choice** (client mapping vs previous Dapper usage) is mostly about C# mapping, DI, and migrations.
- **Query performance** for an existing stored procedure is dominated by SQL Server plans and indexes. Calling the same proc through EF (`FromSqlRaw` / `Database.SqlQuery` / `ExecuteSql` / EF-managed `SqlCommand`) keeps that server-side work; it does not require rewriting procs as LINQ.

Target end state (achieved for client library):

- One primary library: EF Core in `QueenZone.Data`.
- Keep valuable stored procedures (especially `ModernForum_*` and proven legacy procs) and invoke them from EF-backed repositories.
- Use EF entities/change tracking for writes and for simple modern-table reads.
- Do not map the entire legacy schema into EF entities solely to call procs; keyless entities, `SqlQuery` row types, or `EfSql` readers are enough for read models.

This target is independent of [ADR 0021](0021-legacy-database-is-production.md) (which retired ADR 0004's "legacy as import source" framing — the legacy database is the permanent production database, not a source feeding a separate one). Table shape (legacy vs modern) remains independent of the client library either way.

## Consequences

Benefits:

- Admin write code uses EF `ExecuteUpdate`, `ExecuteDelete`, and `SaveChanges` instead of manual SQL strings.
- Modern schema is versioned through EF migrations.
- Public read queries stay explicit (SQL/procs) where that is the right tool, but share one client library and DI lifetime model with writes.
- Contributors have a single matrix and a clear default for new work.
- One NuGet data-access dependency surface for SQL Server in `QueenZone.Data`.

Tradeoffs:

- `NEWS_T` is mapped with `NEWS_ID` as the EF key; duplicate legacy rows are read through a latest-row SQL projection.
- Deployments must apply both bootstrap SQL scripts (where required) and `dotnet ef database update` for EF-managed tables.
- Stored procedures that need output parameters use a small ADO.NET helper (`EfSql`) on the EF-managed connection rather than pure `SqlQuery` composition.
- Public-read repositories are scoped (not singleton) because they depend on `QueenZoneDbContext`; sitemap builders are scoped to match.
