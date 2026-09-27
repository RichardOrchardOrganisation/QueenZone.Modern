# Public Query Cache

The public site caches a small set of stable, anonymous query results in ASP.NET Core `IMemoryCache` through `PublicQueryCacheService`.

This is a **process-local** cache. Production runs a **single** App Service worker (B1); multi-instance Redis-backed cache is intentionally **not** used for cost reasons. See [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md).

This cache is intentionally limited to shared public data:

- homepage latest published news
- public news archive pages and published counts (including decade/year filters)
- public article latest lists, archive pages, and published count
- forum archive category and thread statistics
- photography category lists and category pages
- homepage "On This Day", nearby-history snippets, and the published timeline
- published quote and trivia pools (random pick stays per request)
- biography chapters and discography albums

Admin, personalized, authenticated, preview, and edit workflows must not read from this cache.

`/warmup` primes the homepage-shaped entries (latest news, published counts, forum stats, on-this-day, photo categories) after deploy. The nine reads run concurrently, each in its own DI scope so EF repositories do not share a `DbContext`. See [`azure-hosting-plan.md`](azure-hosting-plan.md) for the duration budget.

## Freshness

Default durations are configured by `PublicQueryCacheOptions`:

| Data | Default duration |
| --- | --- |
| Latest news, news archive pages, and public news counts | 5 minutes |
| Public article latest / archive / count | 30 minutes |
| Forum archive statistics | 30 minutes |
| Photography categories / category pages | 30 minutes |
| Homepage history snippets and published timeline | 12 hours |
| Catalog slices (quotes, trivia, biography, discography) | 30 minutes |

Deployments can override these values with the `PublicQueryCache` configuration section. Short TTLs are preferred for editorial data, while forum, photo, and history data can tolerate longer staleness because those slices are mostly archive content.

## Invalidation matrix

Keys live in `PublicQueryCacheKeys`. Invalidation APIs live on `PublicQueryCacheService`. Prefer **versioned keys** (bump a version entry) when many key variants exist; prefer **explicit `Remove`** when the key set is fixed and small.

| Cache entry | Key pattern | Default TTL | Invalidation API | Mechanism | Call sites (must stay in sync) | Fallback if missed |
| --- | --- | --- | --- | --- | --- | --- |
| Latest news | `public-query:news:latest:v{version}:{count}` | 5m (`NewsCacheDuration`) | `InvalidateNewsCache` | Bump `public-query:news:version` | Admin news publish / unpublish / delete (`Admin/News/Action`); edit of published news (`Admin/News/EditPost`) | TTL |
| News published count | `public-query:news:published-count:v{version}` | 5m | `InvalidateNewsCache` | Same news version bump | Same as latest news | TTL |
| News archive page | `public-query:news:archive:v{version}:{page}:{pageSize}:decade={}\|year={}` | 5m | `InvalidateNewsCache` | Same news version bump | Same as latest news | TTL |
| Filtered news published count | `public-query:news:published-count:v{version}:decade={}\|year={}` | 5m | `InvalidateNewsCache` | Same news version bump | Same as latest news | TTL |
| Latest articles | `public-query:articles:latest:v{version}:{count}` | 30m (`ArticleCountCacheDuration`) | `InvalidateArticlesCache` | Bump `public-query:articles:version` | Admin article edit (`Admin/Articles/Edit`); article status (`Admin/Articles/Status`) | TTL |
| Article published count | `public-query:articles:published-count:v{version}` | 30m | `InvalidateArticlesCache` / `InvalidateArticleCountCache` | Same article version bump | Same as latest articles; community publish path (`Admin/Articles/Action`) | TTL |
| Articles archive page | `public-query:articles:archive:v{version}:{page}:{pageSize}` | 30m | `InvalidateArticlesCache` | Same article version bump | Same as latest articles | TTL |
| Forum categories | `public-query:forum:categories` | 30m (`ForumStatsCacheDuration`) | `InvalidateForumStatsCache` | `Remove` both forum keys | New thread (`Forum/NewThread`); new post (`Forum/Topic`); some post edits that affect stats (`Forum/EditPost`) | TTL |
| Forum thread count | `public-query:forum:thread-count` | 30m | `InvalidateForumStatsCache` | Same | Same as forum categories | TTL |
| Photo categories | `public-query:photo:categories:v{version}` | 30m (`PhotoCacheDuration`) | `InvalidatePhotoCache` | Bump `public-query:photo:version` | Admin photo writes (`Admin/Photos/*` via `InvalidatePublicPhotoCachesAsync`) | TTL |
| Photo category page | `public-query:photo:category-page:v{version}:{catId}:{page}:{pageSize}` | 30m | `InvalidatePhotoCache` | Same photo version bump | Same as photo categories | TTL |
| On this day | `public-query:history:on-this-day:v{version}:{yyyyMMdd}:{count}` | 12h (`OnThisDayCacheDuration`) | `InvalidateHistoryCache` | Bump `public-query:history:version` | Admin timeline create / edit / delete / publish (`Admin/Timeline/*` via `InvalidatePublicHistoryCacheAsync`) | TTL |
| Around this day | `public-query:history:around-this-day:v{version}:{yyyyMMdd}:{dayWindow}:{count}` | 12h | `InvalidateHistoryCache` | Same history version bump | Same as on this day | TTL |
| All published history | `public-query:history:all-published:v{version}` | 12h | `InvalidateHistoryCache` | Same history version bump | Same as on this day | TTL |
| Published quotes | `public-query:quotes:published` | 30m (`CatalogCacheDuration`) | `InvalidateQuotesCache` | `Remove` fixed key | Admin quote create / edit / delete / publish (`Admin/Quotes/*`) | TTL |
| Published trivia | `public-query:trivia:published` | 30m | `InvalidateTriviaCache` | `Remove` fixed key | Admin trivia create / edit / delete / publish (`Admin/Trivia/*`); approve-from-submission (`Admin/TriviaSubmissions/Action`) | TTL |
| Biography chapters | `public-query:biography:chapters` | 30m | `InvalidateBiographyCache` | `Remove` fixed key | Admin biography create / edit (`Admin/Biography/*`) | TTL |
| Discography albums | `public-query:discography:albums` | 30m | `InvalidateDiscographyCache` | `Remove` fixed key | None today (no album write path); TTL-only until a sync/admin writer exists | TTL |

Related **output-cache** tags (anonymous HTML / sitemaps) are separate from this query cache. Editorial writes that change public HTML should also `EvictByTagAsync` for `PublicOutputCachePolicies.PublicHtmlTag` / `PublicSitemapTag` as already done from admin news, photo, and sitemap invalidation paths. See `PublicOutputCachePolicies` and issue [#321](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/321).

### Contributor rules

1. **Writes that change public aggregates must invalidate.** Admin or member writes that change data exposed through `PublicQueryCacheService` must call the matching invalidate API (or bump the matching versioned key family). Do not rely on TTL alone for editorial news, articles, photos, forum stats, homepage history snippets, quotes, trivia, or biography. Discography is TTL-only until an album write path exists.
2. **New cached queries need a matrix row.** When adding a `GetOrCreateAsync` entry, add a key helper on `PublicQueryCacheKeys`, document the row above, and wire invalidation (or explicitly document TTL-only with rationale, as for discography albums).
3. **Prefer version bumps for open-ended key families.** News latest-count / archive-page / filter variants, article latest/archive pages, and photo category pages use version segments so callers can add new count/page shapes without updating every `Remove` call site.
4. **Single-instance assumption.** Invalidation only affects the current process. Do not design multi-instance consistency on this cache until [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md) allows scale-out and a shared cache product.
5. **Stampede behaviour.** Cold-cache concurrent loads share a process-wide per-key `SemaphoreSlim` gate inside `PublicQueryCacheService` (static `LoadGates`), so scoped service instances still coalesce factory calls. Fixed under [#392](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/392); covered by multi-instance stampede tests in `PublicQueryCacheServiceTests`.

### How to verify the matrix against code

```text
# Invalidation APIs and call sites
rg "Invalidate(News|Forum|Article|Photo|History|Quotes|Trivia|Biography|Discography|FanPerformance)|InvalidateArticleCountCache|InvalidateForumStatsCache" src tests

# Key conventions
rg "PublicQueryCacheKeys" src
```

Re-check this document whenever invalidation call sites or key families change.

## Related

- [`hosting-scale-and-cache.md`](hosting-scale-and-cache.md) — single-instance / no Redis decision
- `src/QueenZone.Web/Caching/PublicQueryCacheService.cs`
- `src/QueenZone.Web/Caching/PublicQueryCacheKeys.cs`
- `src/QueenZone.Web/Caching/PublicQueryCacheOptions.cs`
- Output HTML cache policies: `PublicOutputCachePolicies` (Testing disables HTML output cache for deterministic integration tests; Production-shaped hit coverage lives in `PublicOutputCacheTests`)
