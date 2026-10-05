# Hosting scale and cache model

Decision for QueenZone production (App Service `queenzone-prod`, plan **ASP-Queenzone-Prod**).

## Current production shape (re-verified 2026-09-10)

| Setting | Value |
| --- | --- |
| App Service | `queenzone-prod` |
| Resource group | `Queenzone-RG` |
| App Service plan | `ASP-Queenzone-Prod` |
| SKU / tier | **B1 / Basic** (lowest paid plan in use) |
| Worker / instance count | **1** |
| Always On | enabled |
| Redis / Azure CDN / Front Door | **none** |

Full estate inventory for OpenTofu: [`opentofu-inventory.md`](opentofu-inventory.md).

## Development database sizing

The isolated development database, `queenzone-dev-db`, is deliberately
**Basic / 2 GB**. This is not a production capacity decision. While separate
work completes its legacy baseline, the deployed dev application temporarily
uses deterministic sample data and `deploy-dev.yml` skips migrations.
Production remains independently sized.

**Explicit product decision:** stay on a **single instance** of the current low-cost plan. Do **not** scale out App Service instances and do **not** add **Azure Cache for Redis** (or similar paid distributed cache) unless budget and traffic later justify revisiting this document.

## Why this is enough for now

Public performance work already relies on **process-local** mechanisms that are correct on one worker:

| Mechanism | Behaviour on single instance |
| --- | --- |
| `IMemoryCache` / `PublicQueryCacheService` | Shared for all requests on the one worker |
| ASP.NET Core output cache (sitemaps + anonymous HTML) | In-process store on the one worker |
| News / sitemap / HTML cache invalidation after admin publish | Bumps local version + evicts local output-cache tags |
| Forum post rate limiter (memory + DB probe) | Counts are consistent for the single process |
| Mobile `/api/v1/auth` (IP + per-member in-process) | Same as website login IP policy, plus a per-account cap on sign-in completion and refresh |
| Per-member daily upload quotas (`MemberUploadQuotaService` / `IMemoryCache`) | Count + byte caps per principal per UTC day on the one worker. Website `/submit/photo` and mobile `POST /api/v1/member/photo-submissions` both call `PhotoSubmissionService.SubmitAsync`, which keys the same `member:{guid}` bucket via `PrincipalKeyFromMemberId`. Web and mobile share one cap because they hit this single worker — not because of Redis, and not via a photo-only counter (forum attachments, editor images, and avatars use the same service). |

Those designs become **incorrect or leaky** only if instance count &gt; 1 (stale HTML/news on another worker, rate-limit bypass, invalidation that does not reach every node).

## Public HTML query variation

`PublicOutputCachePolicies.PublicHtmlQueryKeys` is the explicit query contract for
anonymous Razor HTML. Path and route values also distinguish entries. Marketing
parameters such as `utm_source` intentionally reuse the same rendered response.

| Inputs | Consumers / purpose |
| --- | --- |
| `page`, `pageNumber` | Archive pagination, public member activity, and archive-author pagination |
| `size` | Photo category/detail size filtering |
| `slug`, `year` | Retained existing variation keys for archive routes |
| `decade` | Timeline's visible decade and canonical URL |
| `tag` | Community article tag filter on `/articles`; tagged views page with `page=` (canonical remains `/articles`) |
| `cp` | Legacy community pager query; `/articles?cp=N` 301s to `/articles` (tag kept) |
| `scope` | Quiz leaderboard daily, best-run, or total-points view and canonical URL |
| `claim` | Quiz sprint's guest-score claim notice |
| `handler` | Razor Pages GET handler selection, including sprint's start redirect |

The #1947 review checked public GET parameters, bound properties, and direct query
reads. Other detail identifiers come from route templates; search/help/account/admin/
submission surfaces are excluded, and member-only surfaces do not qualify as
anonymous cacheable responses. Existing response-level safeguards (including
Set-Cookie handling) still apply. Variation does not make a private response cacheable.

When a public page gains a content-changing input, update this contract and add a
production-shaped regression test that warms one variant and requests another in
both orders. Assert visible content and canonical metadata, then assert that
identical and tracking-only requests reuse their own variant. Ordinary `Testing`
hosts deliberately bypass HTML output caching; use `ProductionHostFixture` with
sample/fake repositories. Preserve authenticated bypass and editorial tag eviction.
For a feature with a larger independent query contract, prefer a dedicated policy
rather than growing the global list indefinitely.

## Archived / deferred work (cost)

Tracked under epic [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312) Phase D:

| Issue | Title | Disposition |
| --- | --- | --- |
| [#323](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/323) | Distributed cache/rate-limits (Redis) | **Not planned** while on single B1 — closed as not planned |
| [#326](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/326) | Document scale-out readiness | **Done** by this document |

Do not reopen #323 unless this document is updated to allow multi-instance hosting **and** a paid distributed cache (or an accepted alternative).

## Still in scope without Redis or a larger plan

These improve reliability on the current B1 single worker and do **not** require scale-out:

| Issue | Title | Notes |
| --- | --- | --- |
| [#324](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/324) | Azure SQL retry + sane command timeouts | Transient fault handling; no new Azure SKU |
| [#325](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/325) | Readiness health checks (SQL/blob) | Ops signal only; keep `/health` cheap for liveness |
| [#330](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/330) | Per-member daily upload quotas | Process-local; container/size caps still enforced; AV scanning not planned |

## If scale-out is reconsidered later

Before raising instance count above 1:

1. **Budget** — confirm willingness to pay for either sticky sessions alone (still weak for cache invalidation) or, preferably, **Azure Cache for Redis** (or equivalent) for:
   - distributed `IMemoryCache` / public query cache  
   - output-cache store  
   - rate-limit counters (or keep rate limits DB-backed only)
2. **Invalidation** — editorial publish must reach every node (Redis key version, pub/sub, or shared output-cache tag store).
3. **Update this document** — record new instance count, SKU, and cache product.
4. **Reopen or replace #323** with a concrete design and cost note.

Until then, **assume single instance** in all performance and caching designs.

## Mobile offline snapshot budget (device cache, #764 / #762)

This is **not** a production B1 traffic study and it does **not** add Redis, output-cache, or new API caching headers. `#764` asked for payload size vs the current B1 budget before growing the mobile read cache; `#762` reuses the existing device `ContentCache` (AsyncStorage, not SecureStore) for previously opened forum threads and conversations.

Opening those screens today:

| Action | Requests that become the offline snapshot | Extra live requests (not cached) |
| --- | --- | --- |
| Open a forum thread | `GET /api/v1/forum/topics/{id}` + `GET /api/v1/forum/topics/{id}/posts` page 1 | Watch, poll viewer/vote, attachment download |
| Open a conversation | `GET /api/v1/me/messages/{id}` (marks read; cache only after this real open) | Reply / report / archive / block |

Current page sizes (same clamps as the website): `forumPostsPageSize` = 15 (`ForumRoutes.PostsPageSize`), `conversationPageSize` = 50 (`PrivateMessageLimits.ConversationPageSize`).

Representative UTF-8 JSON sizes from the in-memory Testing fixtures / WAF sample shapes (topic `1002` “Ranking every studio album”, a 15-row posts page, a 50-message conversation). Not live Azure traffic:

| Payload | Approx. bytes |
| --- | --- |
| Forum topic header | ~230 |
| Forum posts page (`pageSize` 15, short sample bodies) | ~5 KB |
| Thread open (header + page 1) | ~5.5 KB |
| Conversation (`pageSize` 50, short sample bodies) | ~14 KB |
| News/biography/discography detail (existing cache) | hundreds of bytes to a few KB |

`ContentCache` is one LRU map for archive details **and** these snapshots. A 40-entry cap is enough for ~20 archive details, but 15 recently opened threads (topic + page 1 = 30 entries) plus a handful of conversations would evict news/biography/discography. The device cap is therefore **80** entries: about 20 archive details, 20 threads (topic + first page, plus a few extra opened pages), and ~10 conversations. At the sizes above that is well under 1 MB of JSON, so it does not pressure B1 — the bytes live on the phone, and the server still serves one topic + one posts page (or one conversation) per open.

Do not cache watch state, poll viewer/vote state, attachment bytes, or fan-performance audio in this store.

### Device-cache lifecycle and maintenance (#1948 / #1951)

One `ContentCache` instance owns a storage prefix (the process singleton in
`defaultCache.ts`). It serializes payload/index changes and purges. Network work
holds a short-lived lease: a prefix purge invalidates matching leases immediately,
then removes stored entries behind any already-started storage operation. New
requests join after that purge. Leases are released on completion; no historical
member-key generation dictionary accumulates. Foreground fetch, background SWR,
LRU reads, and request deduplication obey the same boundary. A late 401 from a
previous session cannot delete a newer session's snapshot.

A compact `$lru-v1` record stores only key/access metadata. Hits update that
record, not the complete payload envelope. A warm capacity insertion uses the
in-memory index and reads **zero payloads**, down from 81 at the 80-entry cap.
A warm hit reads its one payload. A restarted cache with valid metadata reads
one compact index plus the requested payload; missing/corrupt metadata triggers
one recoverable payload scan, not a scan for every eviction. Existing version-1
payload envelopes remain readable; unversioned/unsupported payloads still
self-delete. Ties use access sequence then key for deterministic eviction.

The operation-count regressions in `contentCache.test.ts` use instrumented
storage and deferred promises, not elapsed-time thresholds. Read/metadata-write
failures do not make an otherwise available offline payload inaccessible.
These are storage-operation measurements, not native latency measurements.

Measured against baseline `f932aaab` and this implementation with the same
80-entry instrumented in-memory adapter:

| Operation | Before | After |
| --- | --- | --- |
| Capacity insertion | 81 payload reads, 1 key enumeration, 1 payload write, 1 batch removal | 0 reads/enumerations, 1 payload + 1 compact-index write, 1 batch removal |
| Warm read | 1 payload read + full-envelope rewrite | 1 payload read + compact-index write |
| First read after owner restart | 1 payload read + full-envelope rewrite | 1 key enumeration + 1 compact-index read + 1 payload read + compact-index write |

Cold initialization now pays one metadata read/enumeration; its purpose is to
remove the recurring payload scan at capacity, not to claim every operation is
cheaper. The warm-read write count is unchanged, but its bytes are metadata only.


Byte-budget decision: retain the existing 80-entry cap for this change. The
representative measurements above (about 230 B per topic header, 5 KB per short
posts page, 14 KB per short conversation) support the existing typical mix under
1 MB; they do **not** establish worst-case sizes for long bodies. Introducing a
hard byte threshold from these short fixtures alone could unpredictably remove
useful offline conversations. No new byte-limit claim is made. Measure long-body
and native iOS/Android workloads before choosing a byte cap/oversize-entry policy;
large payloads remain a documented limit of the entry-only budget.


## Related docs

- [`azure-hosting-plan.md`](azure-hosting-plan.md) — overall Azure shape  
- [`opentofu-inventory.md`](opentofu-inventory.md) — live estate ownership for OpenTofu  
- [`public-query-cache.md`](public-query-cache.md) — process-local public query cache  
- Epic [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312) — performance / security improvement backlog  
