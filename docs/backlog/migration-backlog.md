# Migration Backlog

Living index of migration work. **Prefer open GitHub epics and issues for active tracking**; this file separates completed foundation work from remaining product/archive gaps so planning docs stay truthful.

## Status index

| Track | Link | Notes |
| --- | --- | --- |
| Architecture / performance (2026-07-23) | [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312) | Earlier review; many Phase A–E children done |
| Architecture / performance (2026-07) | [#391](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/391) | Latest review; P0–P3 children |
| Data access (EF Core + SQL/procs) | [ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md) | Dapper package removed; EF is the client library |
| Hosting scale & cache | [`hosting-scale-and-cache.md`](../architecture/hosting-scale-and-cache.md) | Single B1; no Redis |
| Public query cache | [`public-query-cache.md`](../architecture/public-query-cache.md) | TTLs + invalidation matrix |
| News agent / discovery | [`news-agent.md`](../architecture/news-agent.md) | Worker + admin review queue |
| Modernization plan (phases) | [`modernization-plan.md`](../architecture/modernization-plan.md) | Historical phases + current status |
| Testing policy | [`testing-policy.md`](../architecture/testing-policy.md) | CI gates, layers, coverage |

Do not invent new product scope in drive-by doc edits. Open a GitHub issue when accepting new work.

---

## Open work (high level)

Items below are still useful product/archive gaps or ongoing hardening. Detailed acceptance criteria live in linked issues where present.

### News and editorial

- **Automated discovery hardening** — worker, OpenRouter triage/drafting, and admin review queue are shipped; continue reliability, budget, and editorial-rule work under `docs/architecture/news-agent*.md` and related issues.
- **Modern approved-news tables** — long-term model for non-legacy news rows (historical issue #7 theme); public reads still project from legacy `NEWS_T` shapes via EF SQL.

### Content / archive still thin or unfinished

#### Blog archive feasibility review

Acceptance criteria:

- Blog ownership/profile exposure reviewed.
- Comments policy documented.
- Sample blog post renders read-only.

#### FAQ, quotes, featured sites (legacy map Phase 2 leftovers)

- Public FAQ surface if content remains valuable.
- Quotes / featured sites only if links and copyright policy are clear.

#### Discography polish

- Song pages and lyrics policy if product accepts them (see lyric policy item under completed discography epic if already decided in code/docs).
- Structured metadata where appropriate.

#### Pictures / media ops

- Ongoing Blob / CDN path hygiene (see `AGENTS.md` media serving table).
- Admin photography gallery management tracked in [#349](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/349) where still open.

### Architecture children still open under #391

Filter: [label `architecture-review-2026-07`](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues?q=label%3Aarchitecture-review-2026-07). P0/P1 and P3 doc/test children are largely closed; remaining open themes are mainly **P2 structure** (verify on GitHub before starting):

- Folderize Data/Web, extract NewsAgent.Tests, shared test factory, discovery twin shrink, test-double naming, SQL docs source of truth.

### Hosting exploration (deferred)

Static Web Apps / Functions prototypes remain **optional research**, not the production path. Production is App Service Razor Pages on single B1. Revisit only with an explicit hosting decision update.

---

## Archive / completed foundation

The following early backlog epics are **done**. Kept here so historical acceptance criteria remain searchable; do not re-open as greenfield work.

### Epic: Repository Foundation — **done**

| Item | Outcome |
| --- | --- |
| Create solution skeleton | `QueenZone.sln`, `src/QueenZone.Web`, `src/QueenZone.Data`, `tests/`, local run with sample data |
| Add CI | GitHub Actions build, test, coverage gates, smoke publish |
| Add basic observability | Application Insights wiring, startup/request logging, `/health` |

### Epic: News Vertical Slice — **done** (with ongoing discovery hardening)

| Item | Outcome |
| --- | --- |
| Legacy DB connection | Config-driven; secrets not committed; empty connection uses in-memory sample data |
| Latest news / archive / detail | Shipped with canonical `/news` and `/news/{id}/{slug}` |
| Canonical news URLs + tests | Covered in web integration tests |
| Automated discovery design | Architecture + worker + admin queue shipped; see `news-agent.md` |

### Epic: Articles And Biography — **done** (public archive)

| Item | Outcome |
| --- | --- |
| Articles archive and detail | `/articles` (+ pagination), detail, community submissions path |
| Biography list/detail | `/biography`, canonical detail, sitemap inclusion |

### Epic: Discography — **largely done** (public archive)

| Item | Outcome |
| --- | --- |
| Album list and detail | `/discography` and album detail via EF + procs |
| Core archive treatment | Canonical URLs; further song/lyrics policy may remain product decisions |

### Epic: Pictures — **public path done**; admin/ops may continue

| Item | Outcome |
| --- | --- |
| Picture categories/detail + Blob URLs | Public photography pages + CDN hosts |
| Path audit / migration | Historical import/Blob work landed; ops hygiene continues |
| Admin gallery management | See [#349](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/349) |

### Epic: Forum archive — **done** (and expanded)

| Item | Outcome |
| --- | --- |
| Feasibility + modern schema + import | Modern `ModernForum*` tables; production defaults to `ModernForumRepository` |
| Read-only public browse | Shipped; member write path added later as deliberate feature |

### Epic: SEO And Monetisation — **foundation done**

| Item | Outcome |
| --- | --- |
| SEO foundation | Titles, descriptions, canonicals, sitemap, robots, Open Graph where implemented |
| Monetisation exploration | Policy/docs as decided pre-launch; do not reintroduce ads without product decision |

### Epic: Hosting Exploration — **explored; not production path**

| Item | Outcome |
| --- | --- |
| Static Web Apps / Functions prototypes | Documented under hosting-options exploration; **not** current production |

---

## How to update this file

1. When an epic or large feature ships, move its acceptance bullets into **Archive / completed** (or delete if duplicated by an ADR).
2. Link open work to GitHub issues; avoid long unchecked lists that drift from `main`.
3. Keep data-access and hosting facts aligned with ADR 0006 and `hosting-scale-and-cache.md` — never reintroduce “Dapper as primary client” or multi-instance Redis as defaults.
