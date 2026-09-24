# Frontend LCP / media and anonymous HTML cache headers (#333)

Dated note for issue [#333](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/333).

## Changes

1. **Hero / design-system images** — `_PictureImage` emits intrinsic `width`/`height` from `DesignSystemImageDimensions` and `fetchpriority="high"` for eager `img-hero` LCP candidates; WebP `<source>` unchanged.
2. **Homepage era LCP** — screenshot uses intrinsic `787×518` (was generic `800×600`).
3. **Anonymous HTML `Cache-Control`** — `public, max-age=60` on successful anonymous public HTML via `PublicHtmlCacheControl` (does not override static/UGC/sitemap headers; excluded admin/account/auth).
4. **Analytics** — gtag loader moved to end of `<body>` and deferred with `requestIdleCallback` (fallback: `load`).

## Measurement workflow

Use the existing script (see `frontend-performance-checks.md`):

```powershell
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor mobile -Runs 3
```

Compare medians for `/`, `/news`, a news/article detail path, and `/forum` against `frontend-performance-budgets.json`. Paste before/after tables in the PR.

## Before / after lab capture (local sample data, `-Runs 3`)

Same machine, same command, consecutive runs:

```powershell
powershell -File .\scripts\Measure-FrontendPerformance.ps1 -StartLocalApp -FormFactor mobile -Paths "/,/news,/forum" -Runs 3
```

| Ref | Commit |
| --- | --- |
| **Before** | `origin/main` @ `4002d97` |
| **After** | `grok/frontend-lcp-cache-headers` @ `998d5dd` |

### Median of 3 mobile first-load runs

| Path | Metric | Before (main) | After (branch) | Δ |
| --- | --- | ---: | ---: | ---: |
| `/` | LCP (ms) | 3082 | 3082 | 0 |
| `/` | CLS | 0.00 | 0.00 | 0 |
| `/` | Transfer | 707.3 KB | 707.7 KB | ~+0.4 KB |
| `/` | Requests | 18 | 18 | 0 |
| `/` | Perf score | 92 | 92 | 0 |
| `/news` | LCP (ms) | 2556 | 2555 | −1 |
| `/news` | CLS | 0.00 | 0.00 | 0 |
| `/news` | Transfer | 261.1 KB | 261.2 KB | ~+0.1 KB |
| `/news` | Requests | 13 | 13 | 0 |
| `/news` | Perf score | 95 | 96 | +1 |
| `/forum` | LCP (ms) | 2557 | 2555 | −2 |
| `/forum` | CLS | 0.00 | 0.00 | 0 |
| `/forum` | Transfer | 255.8 KB | 256.0 KB | ~+0.2 KB |
| `/forum` | Requests | 13 | 13 | 0 |
| `/forum` | Perf score | 95 | 96 | +1 |

Raw run folders (gitignored): `docs/performance/results/2026-07-27-161716` (before), `docs/performance/results/2026-07-27-161914` (after).

### Interpretation

- **No core-budget regression.** LCP/CLS/transfer/requests are effectively flat within Lighthouse noise.
- Lab LCP remains slightly over the advisory mobile **2500 ms** cap on all three paths on both main and the branch (simulated throttling); scores 92–96. Not a merge gate.
- This change is mostly **correctness/infra for CWV** (intrinsic dimensions, hero `fetchpriority`, short HTML `Cache-Control`, deferred analytics). Simulated Lighthouse on already-WebP design assets will not show a large LCP drop; expect more benefit in real browsers for CLS/caching/analytics contention.
- Windows chrome-launcher still logs `EPERM` on temp cleanup; reports remain usable (documented in `frontend-performance-checks.md`).

## Expected direction

| Area | Expectation |
| --- | --- |
| LCP | Neutral-to-better on hero pages (dimensions + fetchpriority; WebP already present) |
| CLS | Better or equal where design-system images lacked dimensions |
| Transfer / requests | Neutral (no new third-party tags; analytics still one delayed script) |
| TTFB | Neutral; short HTML max-age helps warm browser/CDN only |

Advisory budgets: mobile LCP ≤ 2500 ms, CLS ≤ 0.1, transfer ≤ 1.5 MB, requests ≤ 40.

## Related

- Script: `scripts/Measure-FrontendPerformance.ps1`
- Budgets: `docs/performance/frontend-performance-budgets.json`
- Output cache (server-side, 90s): `PublicOutputCachePolicies`
