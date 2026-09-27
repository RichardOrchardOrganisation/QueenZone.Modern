# ADR 0018: Mobile Server-State Strategy

## Status

Accepted.

## Context

[Issue #1151](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1151)
(part of the [#1139](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1139)
mobile architecture review) asks for a recorded decision on whether
`src/QueenZone.Mobile` should adopt `@tanstack/react-query` or keep the
bespoke server-state stack it has grown.

The current stack is small, tested, and layered cleanly:

- `src/hooks/useHomeSection.ts` (93 lines) — section-level stale-while-revalidate
  with `pending` / `ready` / `failed` snapshots.
- `src/hooks/useDetailQuery.ts` (65 lines) — single-resource load.
- `src/hooks/usePagedContent.ts` (252 lines) — pagination, pull-to-refresh and
  infinite scroll, plus `PagedRequestCoordinator` for generation-guarded
  cancellation.
- `src/cache/` — `ContentCache`, a bounded 80-entry LRU over AsyncStorage with
  schema versioning (`CONTENT_CACHE_SCHEMA_VERSION`) and prefix-scoped
  private-content purging.
- `src/hooks/usePullToRefresh.ts` — manual refresh fan-out.

34 files consume these hooks. The review identified three genuine gaps:
no request deduplication, no in-memory cache shared across screens, and no
invalidation graph. The third gap has already produced hand-rolled
workarounds — `src/notifications/newsListEpoch.ts` and
`src/notifications/pmUnreadEpoch.ts` are a bespoke pub/sub growing one module
per invalidatable resource.

Two constraints that were not obvious when the issue was filed, and that shape
the decision:

**Sign-out correctness is prefix-scoped.** `ContentCache` purges by key prefix
(`PRIVATE_CACHE_KEY_PREFIX`, `privateMemberCachePrefix` in `src/cache/keys.ts`)
so that `SessionContext.clearLocal` can drop member-scoped content while
retaining public content. `persistQueryClient` persists a query client as a
single serialized blob and expresses no equivalent of "purge everything
member-scoped, keep everything public". The suggestion in #1151 that
`ContentCache` could be reused as a React Query persister does not survive
contact with this requirement.

**Offline downloads land next.**
[Issue #927](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/927)
(offline playback of fan performances, expected within a month of this ADR)
introduces member-pinned binary audio downloads with a persisted manifest. That
is not server state, and it fits neither existing store: React Query garbage-
collects, and `ContentCache` is an 80-entry LRU, while #927 requires explicitly
that a recording the member chose to keep is never silently evicted. It does,
however, require one thing this codebase cannot currently do: the same download
state (`queued` / `downloading` / `downloaded` / `failed` / `removing`) must be
visible from the listing, the detail/player, and the Play All queue
simultaneously.

## Decision

### 1. Do not adopt `@tanstack/react-query`

Keep the bespoke hooks as the sanctioned server-state pattern. The library is
technically compatible (see the investigation notes below) and the bundle cost
is modest, but adopting it "for new surfaces only" would leave a 45k-line app
running two server-state paradigms indefinitely, and the persistence story does
not fit the sign-out requirement above. The cost of the split exceeds the value
of the ~30% of React Query the app is missing.

### 2. Add request deduplication in the fetch layer, not the hooks

An in-flight promise map keyed by `cacheKey` belongs in
`src/cache/fetchCached.ts`. All three hooks route through it, so one change
closes the gap for every caller without touching hook APIs or their tests.
Implemented in `src/QueenZone.Mobile/src/cache/fetchCached.ts` (#1284).

### 3. Replace the epoch modules with one subscribable store primitive

Build a single `useSyncExternalStore`-based store keyed off the existing
`src/cache/keys.ts` namespace, supporting prefix subscription and prefix
invalidation. Retire `newsListEpoch` and `pmUnreadEpoch` onto it. The codebase
currently has no `useSyncExternalStore` usage at all; the epoch modules are the
only shared-state idiom, and they grow linearly with the number of
invalidatable resources.

This primitive is also the home for #927's download manifest state — locally
owned mutable state with a lifecycle, which is a poor fit for a query cache
even in a codebase that had one. Building it here rather than inside #927 keeps
the download feature from inventing a third pub/sub under delivery pressure.
Implemented in `src/QueenZone.Mobile/src/cache/externalStore.ts` (#1285).
#927 download-manifest UI state (`queued` / `downloading` / `downloaded` /
`failed` / `removing`) belongs on this store under `DOWNLOAD_UI_CACHE_KEY_PREFIX`
in `keys.ts`. Binary files and the durable manifest remain a sibling store
(decision 4) — do not implement them here.

### 4. Binary downloads are out of scope of this decision

The #927 download manifest is a durable, non-evicting, member-scoped store
alongside `ContentCache` — not inside it, and not inside the server-state
layer. Its purge wires into `SessionContext.clearLocal` the same way private
content does. This ADR records that boundary so the two stores are not later
merged on the assumption that "offline" means one thing.

### 5. Do not build a general cross-screen query cache

Re-fetch-on-back is currently masked by `ContentCache` rendering the previous
payload instantly, so the user-visible cost is low. The narrow shared state
that #927 needs is covered by decision 3.

**Revisit this ADR when** a third optimistic-mutation surface appears, or when
a screen measurably suffers from re-fetch-on-back. The offline queue
(`src/offlineQueue/`) plus the `overlayQueuedMessages` optimistic overlay in
`src/screens/messages/conversationMeta.ts` is the surface closest to that line
today.

## Investigation notes

Answers to the checklist in #1151, recorded here because they are the evidence
for the decision rather than for a specific implementation.

- **React 19.2 / RN 0.86 / Expo 57 compatibility:** not a blocker.
  `@tanstack/react-query@5.102.8` declares `peerDependencies` of
  `react: ^18 || ^19`, and its only dependency is `@tanstack/query-core`. It
  ships no native modules, so the New Architecture (Fabric / TurboModules) does
  not apply to it. Compatibility is not the reason for declining.
- **Bundle-size delta:** the published package measures ~13.8 kB gzipped
  (~50.7 kB minified) for the core, plus ~32 kB unpacked for
  `@tanstack/react-query-persist-client`. This is the registry's own
  measurement of the published bundle, **not** a measured delta against our
  Hermes bundle — no install-and-export build was run, because the decision
  does not turn on bundle size.
- **`persistQueryClient` over `ContentCache`:** no. The blob-per-client
  persistence model cannot express `ContentCache`'s prefix-scoped
  private-content purge, which sign-out correctness depends on. Preserving both
  would mean writing a custom persister that defeats most of the reuse
  argument.
- **Offline queue and optimistic overlay:** these map onto React Query
  mutations only loosely. `src/offlineQueue/` is a durable, schema-versioned,
  retry-with-backoff store keyed by `operationId` with its own
  `queued` / `sending` / `needs_attention` lifecycle and idempotency keys — a
  stronger guarantee than `useMutation` offers by default. `overlayQueuedMessages`
  merges queue items into the rendered message list by `operationId`, which is
  the same idea as an optimistic update but survives app restart, which
  React Query's optimistic updates do not.
- **Epoch migration cost:** small. Two 33-line modules, one 19-line hook
  (`useNewsListEpochRefresh`), and five non-test consumers
  (`notifications/subscribe.ts`, `screens/home/useHomeScreenData.ts`,
  `screens/messages/useUnreadConversationCount.ts`,
  `screens/news/NewsIndexScreen.tsx`, `widgets/widgetCache.ts`).

## Consequences

- The project owns roughly 500 lines of query infrastructure by choice, and
  will own a little more after decisions 2 and 3. This is accepted.
- New mobile data surfaces have one sanctioned pattern, recorded in `AGENTS.md`.
- The `HomeScreen` eight-callback `usePullToRefresh` fan-out in
  `src/screens/home/useHomeScreenData.ts` is untouched by this ADR. Prefix
  invalidation from decision 3 could replace it later; that is not scheduled.
- No bulk migration of working hooks is scheduled, in either direction.
- If the revisit trigger fires, the shared store from decision 3 is the natural
  seam to swap, since it already centralises invalidation.
