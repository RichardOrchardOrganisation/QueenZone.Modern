# Modernization Plan

## Objective

Rebuild QueenZone as a modern, maintainable Azure-hosted site while preserving the valuable public content from the legacy ASP.NET Web Forms application and re-establishing QueenZone news as a live editorial surface.

The first release is archive-first and visitor read-only. Accounts, public posting, uploads, private messages, newsletters, and community administration are out of scope until the public archive is stable. News is the exception to the archive-only shape: it is the first vertical slice and should support new editor-approved articles through a deliberate editorial workflow.

## Current status (2026-07)

Much of this plan is **shipped** on App Service (single B1 worker). Treat the phases below as historical delivery order and scope guide, not a checklist of unfinished foundation work.

| Area | Status | Where to look |
| --- | --- | --- |
| Solution, CI, health, App Service deploy | Done | `README.md`, `.github/workflows/` |
| Public news + admin editorial | Done | `docs/architecture/news-agent.md` |
| Articles, biography, discography, photography | Done (public archive) | Razor pages under `src/QueenZone.Web/Pages` |
| Modern forum read + member write path | Done | `docs/sql/`, forum pages |
| Data access client library | **EF Core only** (Dapper package removed) | [ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md) |
| Hosting scale / cache model | Single instance; process-local cache | [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md) |
| Remaining architecture/perf backlog | Open epics | [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312), [#391](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/391) |
| Living backlog index | Open vs completed | [`../backlog/migration-backlog.md`](../backlog/migration-backlog.md) |

## Current Legacy Shape

The legacy site is:

- ASP.NET Web Forms on .NET Framework 4.5.
- Mostly VB.NET in `RO.QZ.Web`.
- SQL Server database `MAIN2_DB`.
- Heavy use of stored procedures.
- Old Telerik Web UI controls.
- Old Web Forms controls, master pages, handlers, and user controls.
- Mixed public content and community features in one database.

Legacy database inventory from `db-schema.sql`:

- 129 tables.
- 20 views.
- About 550 stored procedures.
- Full-text catalogs for blog, YouTube, and forum topic search.

## Target Shape

Recommended target (aligned with shipped architecture):

- ASP.NET Core Razor Pages.
- C# for all new code.
- Azure SQL Database.
- **EF Core** as the single data-access library in `QueenZone.Data`, with intentional SQL and stored procedures for hot or legacy-shaped reads (see [ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md)). Do **not** treat Dapper as the primary client; ADR 0003 described the initial bootstrap and is superseded for the library choice.
- Azure Blob Storage for pictures, downloads, and migrated media (with Cloudflare CDN / Worker hosts as documented in `AGENTS.md`).
- Application Insights for telemetry.
- GitHub Actions for build and deploy.
- Markdown ADRs and migration docs from day one.
- Process-local `IMemoryCache` + short-TTL output cache on a **single** App Service instance ([`hosting-scale-and-cache.md`](hosting-scale-and-cache.md)).

Alternative target explored earlier (not the production path today):

- Prerendered/static public site hosted on Azure Static Web Apps.
- Azure Functions for search, contact forms, and import utilities.
- App Service only for backend/admin needs if Functions become too limiting.

See [`hosting-options-exploration.md`](hosting-options-exploration.md) for that exploration. Production remains App Service Razor Pages.

## Principles

- Preserve content first.
- Avoid copying old Web Forms architecture.
- Keep all SQL Server access inside `QueenZone.Data`; use EF Core with explicit SQL/procs where the read model is complex or already proven.
- Prefer clean, stable, search-friendly canonical URLs over preserving legacy URL shapes.
- Treat personal/community data cautiously.
- Make public archive pages safe before bringing back visitor-facing write features.
- Support new news articles through explicit editorial approval rather than public submission or automatic publication.
- Favor small vertical slices over broad rewrites.
- Do not add Redis / multi-instance scale-out while production remains single B1 unless [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md) is updated first.

## Phases

Phases below are the original delivery order. Many items under Phases 0–4 are complete; use the status table above and the [migration backlog](../backlog/migration-backlog.md) for what is still open.

### Phase 0: Repository Setup

- Create clean repo.
- Add docs from this seed.
- Add `.editorconfig`, `.gitignore`, and basic CI.
- Add solution skeleton.

### Phase 1: News Slice

- Implement homepage latest news.
- Implement news archive.
- Implement news detail.
- Use stable, search-friendly canonical news URLs.
- Establish a modern news read/editorial model that can combine legacy archive news with newly approved articles.
- Design automated discovery and AI-assisted draft ingestion as an internal review workflow, not an automatic publisher.
- Deploy to Azure preview.

### Phase 2: Core Content

- Articles.
- Biography.
- FAQ.
- Discography.
- Quotes and featured sites.

### Phase 3: Media

- Picture categories.
- Picture details.
- Blob Storage migration plan.
- Stable canonical media URLs.

Biography, album information, and the picture library are not optional nice-to-haves. They are core archive pillars for the relaunch and should be planned as first-class content sections.

### Phase 4: Archive Areas

- Forum read-only archive (later expanded to modern projected tables + member write path).
- Blog read-only archive.
- Search.

### Phase 5: Future Interactive Features

Possible later features (some partially shipped — admin news, member OAuth, forum writes, submissions):

- Admin-only content editing (beyond news/photos already present).
- Auth (member OAuth and admin Entra exist; expand carefully).
- Community profiles.
- Forum replacement or import (modern forum path exists).
- Newsletter rebuild.

These should be deliberate new features, not ports of old Web Forms pages.
