# Selective legacy→modern table migration evaluation

Issue: <https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/334>  
Epic: <https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312> (P2 architecture)  
Date: 2026-07-27  
Method: **evaluation only** — candidates listed with measured cost; no schema migration in this PR.

## Why this evaluation exists

Forum already paid for a modern projection (`ModernForum*`) and proved multi-second legacy reads can become milliseconds ([forum benchmark](forum-read-benchmark-2026-06-29.md)). Issue #334 asked whether the same pattern should continue for:

1. **`NEWS_T` latest-row CTE** complexity (`PublishedNewsQuery` / public + admin lists)
2. **Any remaining multi-second legacy procs** on public paths
3. **Denormalized stats** for pagination counts

Acceptance for the evaluation:

- [x] Candidates listed with measured cost
- [x] Migration recommendation gated on parity + benchmarks (none recommended as pure perf work)

## How to re-run

Read-only tools (do not commit connection strings):

```powershell
# Option A: env var from Bitwarden / App Service / Local secrets
$env:ConnectionStrings__QueenZoneLegacy = "<not committed>"
powershell -File .\scripts\Run-LegacyPublicReadBenchmark.ps1

# Option B: pull App Service setting via az CLI (value never printed)
powershell -File .\scripts\Run-LegacyPublicReadBenchmark.ps1 -FromAppService `
  -OutCsv .\docs\performance\results\legacy-modern-eval-live-timings.csv
```

SQL-only variant: `docs/sql/007-legacy-public-read-benchmark.sql` (same shapes; inventory + server `DATEDIFF` timings).

Interpret **server-side ms** for engine cost. Client wall-clock from a remote laptop includes ~50 ms Azure SQL RTT floor on this host and is not comparable to App Service co-located latency.

## Live inventory (queenzone-db, 2026-07-27)

| Metric | Value | Notes |
| --- | ---: | --- |
| `NEWS_T` rows | 5,265 | Small archive table |
| `NEWS_T` `DISPLAY = 1` | 5,250 | Public set |
| Distinct `NEWS_ID` | 5,265 | Equals row count |
| **Duplicate `NEWS_ID` groups** | **0** | Latest-row CTE is currently a no-op safety net |
| `IX_NEWS_T_Display_Date` | present | From `docs/sql/003-live-legacy-performance-indexes.sql` |
| `Q_ARTICLE_T` published | 99 | Tiny |
| `PIC_FILES_T` published | 8,909 | Largest cat ~1,794 images (Freddie Mercury) |
| `Q_STAGE_T` published | 149 | Fan performances |
| `ModernForum*` tables / procs | 6 / 16 | Forum modern path already live |

## Measured costs

### News CTE (primary #334 concern)

Server-side engine time (batch `DATEDIFF`, same Azure SQL as production app):

| Query shape | Server ms | Notes |
| --- | ---: | --- |
| Published count via `ROW_NUMBER` CTE | **4** | App uses this shape for pagination total |
| Published count simple (`WHERE DISPLAY = 1`) | **1** | CTE adds ~3 ms today |
| Archive page 1 (CTE, no body, 20 rows) | **7** | Matches ~6 ms note from 2026-06 index review |
| Full sitemap projection (CTE, all titles) | **33** | Heaviest news SQL; still sub-100 ms; HTML/query cache usually absorbs |

Client wall-clock from evaluation host (3 runs, includes network):

| Area | Avg ms | Rows |
| --- | ---: | ---: |
| news-published-count-cte | 57 | 1 |
| news-published-count-simple | 52 | 1 |
| news-latest / archive page 1 CTE | 58–70 | 20 |
| news-archive deep page (offset 1980) | 98 | 20 |
| news-sitemap-all-cte | 173 | 5,250 |
| news-admin page 1 with `ARTICLE` body | 150 | 50 |

**Takeaway:** News is not in the multi-second class forum was in. CTE overhead is real but tiny. Public path already dropped list body LOBs (#354). Output + query cache further hide SQL cost on B1 single-instance hosting.

### Other public legacy paths (spot check)

| Area | Server/client signal | Disposition |
| --- | --- | --- |
| Articles list (99 rows, preview slice) | ~50 ms client; trivial table | **Leave** on legacy |
| Photo categories + counts (16 cats) | ~54 ms client | **Leave**; watch #351 N+1 / full-collection |
| Photo largest cat page 1 (24 of ~1.8k) | ~53 ms client | **Leave**; indexes present |
| Fan performances page 20 | ~57 ms client | **Leave** |
| `Q_BIO_LIST_SP` / `Q_ALBUM_LIST_SP` | ~51–52 ms client | **Keep procs** (small catalogs, proven) |
| Legacy `Q_FORUM_VIEW_PAGE_SP` | Previously multi-second | **Already modernized** — do not re-migrate |

No newly discovered multi-second public proc in this pass. Historical multi-second pressure was forum; photography `Q_PIC_CAT_PAGE4_SP` was already avoided by app SQL (~662 ms historical note on the old proc path).

## Candidate decision table

| Candidate | Measured cost | User impact if slow | Recommendation | Follow-up |
| --- | --- | --- | --- | --- |
| Full modern `PublishedNews*` table + import | News page SQL ~4–33 ms server | Low today | **Do not migrate for perf** | Revisit with live editorial dual-source work |
| Drop / simplify `ROW_NUMBER` CTE | Saves ~3 ms count; 0 dups today | Negligible | **Watch** — keep CTE as safety for future dup writes | Optional: assert no dups in admin write probe |
| SQL view `dbo.PublishedNewsLatest` | Docs-only clarity | None alone | **Optional hygiene** if SQL is centralized in DB | Not required |
| Denormalized published-news count | Count 4 ms → ~0 | Low (cached) | **Not worth table** | Memory/output cache already covers public |
| Articles modern table | 99 rows | None | **Leave** | — |
| Photos modern projection | 9k images; page queries fine | Medium if N+1 returns | **Do not project whole gallery** | Prefer #351 app/query fixes |
| Bio/album/stage procs | Small + already used | None | **Keep procs** | — |
| Forum legacy procs | Multi-second (historical) | High | **Done** (modern path) | Maintain `ModernForum_RefreshReadStats` |

## Recommendations

### Do now (this evaluation)

1. Treat #334 as **closed evaluation**: no forum-style news modern table for performance.
2. Keep `PublishedNewsQuery` latest-row rules until product moves **new** articles off `NEWS_T` or live data grows duplicate `NEWS_ID`s.
3. Prefer remaining epic B2 work (e.g. photography N+1 #351) over news CTE rewrite.

### Do later only if triggers fire

| Trigger | Action |
| --- | --- |
| Live editorial requires modern live-news tables (product) | Design unified public query (legacy archive + modern live) with parity tests — **product** work, not pure perf |
| Duplicate `NEWS_ID` groups appear or admin writes create dups under load | Keep CTE; consider unique constraint / write-path fix |
| Server sitemap or deep archive exceeds ~100–200 ms under load | Indexed view or thin projected latest-row table **with** benchmarks + parity |
| Photo category paths regress | Indexes + #351, not a full modern gallery schema |

### Explicit non-goals from this pass

- Do not rewrite proven small procs (`Q_BIO_*`, `Q_ALBUM_*`) as LINQ or modern tables for symmetry.
- Do not dual-write `NEWS_T` and a modern table without a publish-path product decision.
- Do not expect forum-scale speedups on `/news` (baseline already single-digit ms server-side).

## Parity note (if a future migration is approved)

Any future modern news read model must ship with:

1. Count parity: published distinct ids vs modern rows  
2. Spot check: 10 oldest, 10 newest, 10 random detail pages  
3. Benchmark script extended with old vs new timings (same pattern as forum 005/006)  
4. Admin list + public list + sitemap + slug routes green  

Until then, **no migration PR** is justified by the measurements above.

## Artifacts

| Path | Role |
| --- | --- |
| `docs/sql/007-legacy-public-read-benchmark.sql` | Read-only SQL harness |
| `scripts/Run-LegacyPublicReadBenchmark.ps1` | Client runner + server `DATEDIFF` section |
| `docs/performance/results/legacy-modern-eval-live-timings.csv` | Optional machine-readable client timings from a run |

## Related

- ADR / matrix: `docs/decisions/0006-hybrid-ef-core-admin-writes.md` (`PublishedNewsQuery`)
- Data policy: `docs/architecture/data-migration-plan.md` (leave `NEWS_T` as archive; modern for new live articles)
- Evolution: `docs/architecture/database-evolution-plan.md` (migrate only on evidence)
- List LOB fix already shipped: #354
- Forum success precedent: `docs/performance/forum-read-benchmark-2026-06-29.md`
