# Member account lifecycle boundaries

Bounded first extraction for [#1952](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1952).

## Responsibility and transaction inventory

| Responsibility | Callers / dependencies | Owner and atomic boundary |
| --- | --- | --- |
| Registration, sign-in, external and legacy linking, password state | `MemberAccountService`, auth callbacks, admin reviewer service; account and legacy repositories, password hashing | Existing account repository; individual writes retain their current save/update boundary |
| Profile, avatars, privacy and social links | `MemberAccountService`; account repository, validation, blob storage | Existing service orchestration; account repository persists profile state |
| Member reporting and lookup | Admin dashboard/member lookup, recipient search, member services | Existing account repository; read-only projections |
| Deletion request and cancellation | `MemberAccountService`; account and retained content attribution | `EfMemberAccountRepository` owns each execution strategy and database transaction, including audit rows |
| Personal-data purge and blob outbox creation | Account deletion maintenance and immediate deletion; cross-domain contributions, attribution, external logins, avatars | `EfMemberAccountRepository.PurgeDeletedAccountsAsync` owns the complete retryable transaction, including retained Apple token rows, audit, anonymisation and durable blob outbox creation |
| Blob outbox consumption and deletion receipts | `MemberAccountService`, `MemberDeletionReceiptService`; blob storage and account repository | Existing post-purge service orchestration; failed blob deletion leaves the durable row pending |
| Apple token protection and remote revocation | `AppleAccountTokenService`; Data Protection, HTTP client, Apple options and logging | Web service owns HTTP retries across scheduled runs; no remote call joins the purge transaction |
| Apple revocation persistence (extracted) | `IAppleRevocationRepository`; external logins and account purge status only | EF component uses the caller's existing context; in-memory component uses the account repository's existing lists and lock |

## Selected boundary

`IAppleRevocationRepository` exposes only saving an opaque protected token,
listing pending revocations, and acknowledging a successful revocation.
`AppleAccountTokenService` no longer depends on identity editing, passwords,
reporting, deletion scheduling, or cross-domain purge operations.
`EfAppleRevocationRepository` owns the three persistence queries, and
`InMemoryAppleRevocationRepository` owns the equivalent list operations. The
in-memory component cannot access social links, deletion audits or blob outbox
state. This is a capability and dependency boundary, not a partial-class split.

`IMemberAccountRepository` inherits the narrow interface as a compatibility
facade. Its existing methods and the concrete repositories' constructor
signatures remain available. DI aliases the narrow capability to the same
account repository, preserving the SQL scoped and in-memory singleton
lifetimes, existing test substitutions, and one shared state owner. The facade
composes its persistence component without creating a context, copying state,
or acquiring a second lock.

## Preserved guarantees

- Deletion request, cancellation and purge stay together in the existing account
  repository. Their execution strategies, transaction commits, contribution
  removal, audit rows and blob outbox creation are unchanged
- Purge keeps Apple login rows with protected tokens and anonymises their email.
  Pending revocations are visible only for accounts with `PersonalDataPurgedAt`;
  completion still requires both the Apple provider and a purged account
- Reads do not consume pending work. A failed Apple HTTP request keeps the token
  for the next scheduled run. Only a successful response triggers completion
- Saving a token still requires the matching member, provider and provider key;
  missing logins are not created. The Data layer receives ciphertext only
- The extracted EF component does not create or commit transactions. Its commands
  join the existing context transaction when one exists; the account repository
  remains the purge transaction and retry owner
- In-memory persistence shares the same lock as purge and now explicitly orders
  pending logins by `LinkedAt` before applying the limit, matching the existing
  SQL ordering rather than depending on insertion order
- Public routes, JSON contracts, schemas, EF model snapshots and SQL-provider
  branching are unchanged. Broader member-service extraction is deferred

## Verification

`AppleRevocationRepositoryContractTests` runs the same matching, eligibility,
ordering, limit, retry retention, completion guard and idempotency cases against
both extracted components. The EF subclass also checks that facade writes and
completion roll back in a caller-owned transaction. Registration tests assert
that both interfaces share the existing repository and its lifetime.

`EfMemberAccountRepositoryTests.Purge_RetainsAppleRevocationAndBlobOutboxUntilEachIsCompleted`
checks the compatibility facade through the full SQLite purge: only the protected
Apple login remains, its email is anonymised, and the deletion receipt remains
incomplete until both Apple revocation and queued avatar blobs are completed.
Existing `AppleAccountTokenServiceTests` cover failed HTTP revocation followed by
a successful retry; account service/repository and deletion-host tests remain
part of regression verification.

Required before publication: the repository's default restore, Release build,
format, full test and coverage checks from `AGENTS.md`. Run the opt-in
`scripts/Probe-MemberAccounts.ps1` only against the guarded SQL Express mirror
when available; SQLite is not evidence of SQL Server verification.

For the 2026-09-30 local preparation, .NET tests, build, format, coverage and the
mirror probe were **not run**: this executor has no `dotnet`, `pwsh`, configured
Six Labors licence or SQL Express mirror. Static diff checks are not a substitute
for those gates. No production data was used for write verification.
