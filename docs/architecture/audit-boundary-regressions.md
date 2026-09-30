# Audit boundary regression ownership

Follow-up for #1953. Concrete regressions belong with their fixing issue; this
inventory does not duplicate their suites or reopen the fixed token-rotation bug.

| Scenario | Owner | Existing fixture / owning tests |
| --- | --- | --- |
| Timeline decade, article cp/tag, leaderboard scope cache variants, both request orders | #1947, separately implemented by Richard | `PublicOutputCacheTests`, `ProductionHostFixture`; excluded from this patch |
| Private network completion after purge, background SWR, read/write storage races, same-member re-login | #1948 | Mobile `contentCache.test.ts`, `withOfflineCache.test.ts`, `fetchCached.test.ts` |
| Same-key single flight, cancel/factory failure, unrelated keys and version/page cardinality | #1949 | `PublicQueryCacheServiceTests` |
| Download cleanup rejection/stall, pending-send confirmation/cancellation, stale session completion, credential/identity pairing | #1950 | Mobile `SessionContext.test.tsx`, `tokenStore.test.tsx`, download manager and offline queue/flusher tests |
| Cold/warm cache I/O counts, deterministic LRU/capacity, metadata recovery | #1951 | Mobile `contentCache.test.ts` instrumented storage; budget decision in `hosting-scale-and-cache.md` |
| Provider revocation capability parity, transaction rollback and deletion outbox retention | #1952 | `AppleRevocationRepositoryContractTests`, `EfMemberAccountRepositoryTests`, existing Apple service tests |

## Sign-out cleanup failure boundary (#1950)

In-memory authentication reset is independent of ancillary cleanup. Deferred
operations carry session ownership, credential operations are ordered, and
confirmed pending-send discard prevents the old in-process queue from continuing
after sign-out. Cleanup errors remain observable and retryable; they are not
reported as successful disk erasure.

There is a remaining durability limit: if physical deletion and recording the
discard intent both fail, a process crash/restart can leave old private data and
pending sends on disk. An in-memory cancellation flag cannot prove durable erasure.
The warning must tell the member that cleanup is incomplete and to retry before
closing the app or signing back in. No blanket success claim is made for this
failure case. Requiring explicit consent before resuming stored sends on every
fresh/restored login would be a separate product change, not silently added here.

## Already-fixed token rotation (#1929 / PR #1931)

The current main baseline already contains
`tests/QueenZone.SqlServerTests/MobileAuthGrantRepositorySqlServerTests.cs`:

- `TryRotate_RevokesStoresAndLinksInOneCommit`: atomic revoke/store/link and replay rejection
- `TryRotate_RollsBackTheRevokeWhenTheStoreFails`: duplicate successor insert fails and old grant remains unrevoked/unlinked
- `TryRotate_ConcurrentRotationsOfOneGrantHaveOneWinner`: four independent contexts, one winner and one stored successor
- `NewContext` enables the production SQL Server retrying execution strategy

SQLite `EfMobileAuthGrantRepositoryTests` also covers atomic outcome, missing/replayed
grants and insert-failure rollback. `MobileAuthServiceTests` covers losing refresh
races, retry guidance, and recovery within the replacement-token grace window.
No duplicate implementation or tests are added here.

Remaining SQL-specific gap, owned by **#1953**: these existing tests enable retry
but do not inject a transient SQL failure or ambiguous commit acknowledgement to
prove a replayed execution-strategy delegate remains safe. That is distinct from
SQLite rollback and from simply running with `EnableRetryOnFailure`. Add a
controlled fault-injection case in the existing SQL Server test project when that
fixture is available; do not replace it with a SQLite-only claim.

## Verification boundaries

`Testing` remains deterministic/in-memory and still disables HTML output caching.
Production-shaped output-cache behavior uses `ProductionHostFixture`; no real
connection string is introduced into `Testing`.

Deferred promises establish mobile race order; instrumented operations and retained
gate counts establish maintenance bounds. No RSS, arbitrary sleeps, production
load tests or new coverage thresholds are introduced. Existing unrelated tests may
use their established timing helpers.

Before publication, run mobile preflight/coverage and the repository's full .NET
checks. Device sign-out proof, SQL Server tests and opt-in SQL Express mirror probes
must be reported separately. This cloud executor has no .NET SDK, PowerShell,
Six Labors licence, SQL Server mirror or native device; writing a regression is not
evidence that those checks executed. #1947 verification belongs to its separate fix.
