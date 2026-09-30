# Audit boundary regression ownership

Follow-up for #1953. Concrete regressions belong with their fixing issue; this
inventory extends the existing token-rotation suite at its commit-retry boundary.

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
The retry regressions below extend those existing fixtures.

The #1953 follow-on adds two real-SQL fault-injection regressions to that fixture:

- `TryRotate_TransientFailureBeforeCommitRollsBackAndRetriesOneSuccessor`: injects SQL error
  40613 before commit after verifying the revoke/insert/link within the transaction;
  requires two distinct attempts, one rollback, one commit and one linked successor
- `TryRotate_LostCommitAcknowledgementRetriesWithoutDuplicatingAndReturnsSuccess`:
  injects the same transient error after the real commit; requires two distinct
  attempts, exactly one linked successor and a truthful successful result

Both retain the production SQL Server retry strategy and reuse the existing
`SqlExceptionFactory`. Retry/count/data assertions precede the result assertion.
They are not SQLite substitutes or fault-free retry-configuration checks.

Execution status: CI run `36700628300`, SQL job `109840024819`, executed the
real SQL Server suite at audit commit `cd4cd45`: 96 tests passed and the
lost-acknowledgement test failed at its final `Assert.True` with an actual result
of false. The preceding retry/count and exact single-successor persistence
assertions passed. The before-commit fault case passed. No production database
was used.

The correction keeps acknowledgement state local to one repository invocation.
After that invocation successfully stores its replacement, an execution-strategy
retry may return true only when the old grant links to the exact persisted
successor (id, hash, member, client, timestamps and revocation/link state).
A separate replay starts with no such acknowledgement state and still returns
false, even with the same replacement entity. Both SQLite and real SQL Server
regressions cover that separate-replay boundary. Confirmation of the correction
on real SQL Server remains pending the follow-up CI commit; #1953 is not yet
claimed complete.

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
