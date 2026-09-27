# Code Duplication Analysis

Findings from a repository-wide duplication scan (September 2026). This is an
analysis, not a decision: each candidate has a GitHub issue that owns the
refactor design. Line numbers are approximate and drift as code changes; use the
method names to find them.

## Scope and method

Production code only. Excluded: tests, EF migrations, `*.Designer.cs`,
`wwwroot`, generated native mobile output, `node_modules`.

Two complementary passes:

1. **Token-based clone detection (jscpd)** over C#, TypeScript and TSX,
   minimum 80 tokens / 10 lines. Finds textual copy-paste.
   - 115 clones, about 2,200 duplicated lines.
   - C# about 2.1% duplicated, mobile TSX about 0.5%, mobile TS about 0.1%.
2. **Roslyn syntax-tree similarity** over C# methods of 60 tokens or more.
   Each method body is tokenised from its syntax tree with identifiers collapsed
   to one bucket, string literals to one and numeric literals to one. Keywords,
   operators and punctuation are kept. Methods are compared by Jaccard
   similarity of 8-token shingles; pairs at 0.80 or above are reported and then
   clustered.
   - 1,854 methods analysed, 576 similar pairs, 125 clusters.
   - Catches "same code, different names" clones that jscpd misses, such as
     renamed variables and different entity types.

**Caveat on the Roslyn pass:** because identifiers and literals are ignored,
structural similarity does not mean the code is interchangeable. Declarative
code such as EF entity mappings scores high while differing in every column
name and length. Every candidate below was classified by hand, and the
"leave alone" section records the ones that should not be merged.

Neither pass finds code that does the same thing with different structure.

## Overall picture

Duplication is low and concentrated. Most of it comes from one design habit:
each feature (photo, fan-performance, news-suggestion, article, quiz-question,
trivia submissions; the news-agent and search-reindex background jobs) was
built by copying its predecessor. The result is families of near-identical
classes. The largest payoff is in the submission family.

## Candidates

Priority reflects payoff versus risk.

| # | Candidate | Size | Priority | Issue |
|---|---|---|---|---|
| 1 | `GetCurrentMemberIdAsync` copied into Razor page models | 14 exact copies | High, trivial | [#1757](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1757) |
| 2 | Periodic background-service loop | 4 hosted services | High, small | [#1758](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1758) |
| 3 | Submission workflow and status helpers | 4 workflows, 2+ status classes | High, small | [#1759](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1759) |
| 4 | EF submission repositories share one skeleton | 5 repositories, about 10 methods each | High, large | [#1760](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1760) |
| 5 | In-memory submission repositories share logic | 5 repositories | Medium | [#1761](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1761) |
| 6 | News-agent and search-reindex plumbing | lease stores, run-request repos, entity configs | Medium, small | [#1762](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1762) |
| 7 | Razor page-model boilerplate (confirmation, admin edit, inbox pages) | 5 + 6 + 2 pages | Medium | [#1763](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1763) |
| 8 | API endpoint mapping and handler boilerplate | 6 + 5 + 4 + 3 handlers | Medium | [#1764](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1764) |
| 9 | Options validators (already factored, no issue) | 5 validators | None | none |
| 10 | CSV importer and `Tools` import command scaffolding | 4 importers, 3 commands | Medium | [#1765](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1765) |
| 11 | `Tools` CLI usage/argument scaffolding, plus `QueenLink` duplicate | 8 commands | Low | [#1766](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1766) |
| 12 | Image upload/processing pipeline | 4 files | Medium | [#1767](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1767) |
| 13 | EF repository utilities (unique-violation check, intra-file blocks) | 3 + several | Low | [#1768](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1768) |
| 14 | Mobile API request helpers and screens | 2 files + 4 screens | Medium | [#1769](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1769) |

### 1. `GetCurrentMemberIdAsync` in Razor page models

Identical method in at least 14 page models (`MySubmissions`, `Settings`,
`FanPerformances/Report`, every `Submit/*` page, `ArticleConfirmation`, and
others):

```csharp
var authResult = await HttpContext.AuthenticateMemberAsync();
if (!authResult.Succeeded || authResult.Principal is null) { return null; }
var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
return Guid.TryParse(idValue, out var id) ? id : null;
```

Suggested: a `MemberPageModel` base class, or an `HttpContext` extension
(`GetAuthenticatedMemberIdAsync`). The extension is lower-risk because it does
not constrain the page-model inheritance chain.

### 2. Periodic background-service loop

`FanPerformanceSubmissionPurgeHostedService`,
`MemberAccountDeletionHostedService`,
`PrivateMessageReportPurgeHostedService` and `GalleryOrphanSweepHostedService`
all implement: delay after startup, then loop { create scope, run job, swallow
and log non-cancellation errors, delay interval }.

Suggested: a `PeriodicScopedHostedService` base class taking job name, startup
delay, interval and a delegate. The startup-delay rationale comment
(`WEBSITES_CONTAINER_START_TIME_LIMIT`, #666) then lives in one place.

### 3. Submission workflow and status helpers

`FanPerformanceSubmissionWorkflow`, `PhotoSubmissionWorkflow`,
`QuizQuestionSubmissionWorkflow` and `TriviaFactSubmissionWorkflow` repeat
`CanTransition` and `TryValidateStatusChange`. `QuizQuestionSubmissionStatus`
and `TriviaFactSubmissionStatus` repeat `All`, `IsKnown`, `IsPendingReview` and
`Normalize`.

Suggested: a small generic status-set helper, and a transition-table-driven
workflow so each submission type declares only its allowed transitions. The
transition tables are business rules, so they should remain declared per type.

### 4. EF submission repositories

`EfPhotoSubmissionRepository`, `EfFanPerformanceSubmissionRepository`,
`EfNewsSuggestionRepository`, `EfArticleSubmissionRepository`,
`EfTriviaFactSubmissionRepository` and `EfQuizQuestionSubmissionRepository`
repeat the same skeleton for:

- `GetBySubmitterAsync` and `GetDraftsForMemberAsync` (exact match across four)
- `GetPendingAsync`, `PendingQueueQuery`, `MemberQueueQuery`
- `GetDashboardCountsAsync`, `...InMemoryAsync`, `...ViaSqlAggregateAsync`
- `GetTopContributorsInMemoryAsync`, `...ViaSqlAggregateAsync`
- `ApproveAsync`, `RejectAsync`, `PromoteAsync`
- `CreateAsync`, `Map`

They differ in entity type, projected columns and a few status rules. There is
also a repeated `IsSqliteDatabase()` branch that projects columns twice (once
for SQLite tests, once for SQL Server).

Suggested: an `EfSubmissionRepository<TEntity, TModel>` base (or composed
query helpers) that owns paging, the SQLite/SQL Server split, dashboard counts
and top-contributor aggregation, leaving per-type mapping and promotion in the
concrete classes. Must follow [ADR 0006](../decisions/0006-hybrid-ef-core-admin-writes.md),
and the SQL Server-only paths need the opt-in legacy probes described in
`AGENTS.md`. This is the largest and riskiest item; do it after #3 and split
it into per-method-group PRs.

### 5. In-memory submission repositories

`InMemory*SubmissionRepository` repeat `GetBySubmitterAsync`, `GetPendingAsync`,
`GetTopContributorsThisMonthAsync`, `GetDashboardCountsAsync` and the
approve/reject/promote transitions.

Do not merge the EF and in-memory implementations of a repository: the
in-memory one exists to run without a database. Share only the pure logic
(paging arithmetic, dashboard-count aggregation, top-contributor grouping,
transition rules) through static helpers.

### 6. News-agent and search-reindex plumbing

- `SharedNewsAgentLeaseStore` and `SharedSearchReindexLeaseStore` are identical
  apart from the class name (38 lines).
- `InMemoryNewsAgentRunRequestRepository` and
  `InMemorySearchReindexRunRequestRepository` repeat 23 lines.
- `NewsAgentRunRequestEntityConfiguration` and
  `SearchReindexRunRequestEntityConfiguration` are a 1.00 match, as are the two
  lease entity configurations.

Suggested: one parameterised lease store and one run-request repository base.
The two entity-configuration pairs should stay separate tables, so share the
helper, not the table.

### 7. Razor page-model boilerplate

- `Submit/*Confirmation` pages (`Article`, `FanPerformance`, `Photo`,
  `QuizQuestion`, `Trivia`) repeat `OnGetAsync`.
- `Admin/{Biography,Polls,Quizzes,Quotes,Timeline,Trivia}/Edit` repeat
  `OnGetAsync` (load by id, 404 if missing).
- `Messages/Archived` and `Messages/Index` repeat auth check, inbox load and
  pagination set-up (21 lines).

Suggested: a confirmation base page model and a generic admin-edit base; a
shared inbox page base. Razor Pages inheritance is fine but keep the models
explicit about their handlers.

### 8. API endpoint mapping and handler boilerplate

Under `QueenZone.Web/Api/`:

- Six `MapContent*ApiEndpoints` methods repeat the same route-group set-up.
- List handlers (`GetAlbumsAsync`, `GetPhotoCategoriesAsync`,
  `GetQuizzesAsync`, `GetTimelineEventsAsync`, `GetCategoriesAsync`) and detail
  handlers (`GetArticleDetailAsync`, `GetAlbumDetailAsync`,
  `GetQuizDetailAsync`, `GetCategoryAsync`) share pagination, not-found and
  Problem Details handling.
- `GetInboxAsync` / `GetArchivedInboxAsync` and the three `Submissions` list
  handlers repeat a paged-list shape.
- `MessagesApiEndpoints` repeats a 23-line block within itself.
- `MapContactApiEndpoints`, `MapDevicesApiEndpoints` and
  `MapNotificationPreferencesApiEndpoints` repeat set-up.

Constraint: [ADR 0010](../decisions/0010-versioned-json-api-conventions.md) and
`docs/architecture/json-api-v1.md` fix the contract. The refactor must not
change response shapes, and v1 must stay stable for installed store builds
([ADR 0019](../decisions/0019-api-versioning-convention.md)).

### 9. Options validators

**No issue raised.** `AuthRateLimitingOptionsValidator`,
`FanPerformanceRateLimitingOptionsValidator`, `MutationRateLimitingOptionsValidator`,
`UploadQuotaOptionsValidator` and `PasswordSignInLockoutOptionsValidator` scored
as similar, but the shared logic is already in `OptionsValidation`
(`RequirePositiveAtMost`, `Result`). What remains is a thin per-options wrapper
that is clearer left explicit.

### 10. CSV importers and import commands

- `QueenHistoryCsvImporter`, `QuizCsvImporter`, `QuoteCsvImporter` and
  `TriviaFactCsvImporter` repeat `ReadRows` scaffolding (`TextFieldParser` set-up,
  header check, empty-row skipping, column-count check).
- `ToolsApp.RunImportHistoryAsync`, `RunImportQuotesAsync` and
  `RunImportTriviaAsync` are a 1.00 structural match (226 tokens).

Suggested: extend the existing `CsvImportRowParsing` with a shared reader, and
a generic import-command runner.

### 11. `Tools` CLI scaffolding

Eight commands repeat usage text and argument parsing (`WriteUsage` /
`PrintUsage`). `CheckPhotosCommand`, `GeneratePhotoThumbsCommand`,
`PhotoDimInventoryCommand` and `BackfillPhotoDimensionsCommand` share
batching/reporting blocks. `CheckLinksCommand` also duplicates 30 lines from
`Data/Links/QueenLink.cs`, which is a real shared-code candidate.

### 12. Image processing pipeline

`EditorImageProcessor`, `PhotoSubmissionImageProcessor`,
`NewsArticleImageService` and `EditorImageUploadEndpoints` repeat validation
and processing blocks (24-25 lines). Suggested: a shared image pipeline in
`QueenZone.Storage` or a `Web` helper, in line with
`docs/architecture/blob-storage-ugc.md`.

### 13. EF repository utilities

`IsUniqueConstraintViolation` is copied into `EfDeviceTokenRepository`,
`EfHomePollRepository` and `EfPrivateMessageRepository`. Also worth a look:
`EfForumPollRepository` (28 lines repeated within the file), `EfPhotoRepository`
(21 lines within the file) and `EfPhotoSubmissionRepository` (26 lines within the
file). Suggested: a single `DbUpdateExceptionExtensions.IsUniqueConstraintViolation`.

### 14. Mobile (`src/QueenZone.Mobile`)

- `src/api/contact.ts` and `src/api/submissions.ts` repeat 22 lines of request
  and error handling, including `readProblemDetail`.
- `ContactScreen` / `SuggestNewsScreen` (20 lines) and `StoryScreen` /
  `NewsStoryScreen` (20 lines) share layout that can become components.

Constraints: `AGENTS.md` forbids adding a server-state library or a new pub/sub
module. Extract plain helpers and components only. Mobile PRs must pass
`npm run preflight`.

## Reviewed and deliberately not recommended

| What | Why it stays |
|---|---|
| `Ef*Repository` vs `InMemory*Repository` (Quiz 54 lines, HelpRequest 45, PhotoSubmission 35, MemberPublicActivity 29, NewsDiscovery 24) | Largest by line count but intentional: two implementations of one interface, EF for production and in-memory for sample data. Share pure logic only (#5). |
| `LegacyForumRepository` vs `ModernForumRepository` (`GetCategoryTopicsPageAsync`, row DTOs) | Legacy row types are tied to legacy column types (`smallint` etc.; see the `Int16` note in `AGENTS.md`). Legacy and modern forum reads are also expected to diverge. |
| EF `IEntityTypeConfiguration` classes (about 44 `Configure` methods score 0.80+) | Declarative mappings. They look identical only because column names, lengths and index names are ignored by the similarity metric. Abstraction would obscure the schema and risks changing the EF model snapshot. At most, extract small helpers for repeated audit-log and report shapes, and confirm `has-pending-model-changes` stays clean. |
| CSV importer `Map`/upsert bodies | Row shapes and uniqueness rules differ per content type. |

## Reproducing

jscpd (no repository changes; runs via `npx`):

```bash
npx jscpd src --format "csharp,typescript,tsx" --min-tokens 80 --min-lines 10 \
  --ignore "**/Migrations/**,**/node_modules/**,**/bin/**,**/obj/**,**/*.Designer.cs,**/wwwroot/**,**/*Tests*/**"
```

The Roslyn pass was a throwaway file-based .NET app using
`Microsoft.CodeAnalysis.CSharp` and the normalisation described above. It is
not committed because file-based apps inherit the repository's central package
management and need a small workaround; it can be added under `scripts/` if a
repeatable check is wanted.
