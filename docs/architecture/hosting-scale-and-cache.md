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

## Related docs

- [`azure-hosting-plan.md`](azure-hosting-plan.md) — overall Azure shape  
- [`opentofu-inventory.md`](opentofu-inventory.md) — live estate ownership for OpenTofu  
- [`public-query-cache.md`](public-query-cache.md) — process-local public query cache  
- Epic [#312](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/312) — performance / security improvement backlog  
