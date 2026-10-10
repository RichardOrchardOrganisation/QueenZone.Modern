# LOCK-2227 implementation notes

Implemented the Ship B scope for issue #2227. No attachment query, stored procedure, workflow, crawler control, or telemetry configuration was changed. No push, GitHub comment, or PR was made.

## Implementation

- ASP.NET Core 10 includes `Request.Method` in its output-cache key, so a policy that merely accepts GET and HEAD did not share entries. Reference: [OutputCacheKeyProvider](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.0/src/Middleware/OutputCaching/src/OutputCacheKeyProvider.cs).
- Eligible anonymous HEAD requests under `/forum/topic` and `/forum/archive-authors` now run as GET inside the output-cache/render pipeline, with the outgoing body discarded outside the cache (`ShareForumHeadCacheAsync` immediately before `UseOutputCache`). A cold HEAD renders and caches the complete representation; a subsequent GET can reuse it. Method and response stream are restored in `finally`. Authentication, device-theme and automated-test exclusions are retained. There is no database-free HEAD short-circuit.
- ArchiveAuthor Razor endpoints select the dedicated `public-archive-authors` policy (600 seconds). Other Razor endpoints, including topics, retain `public-html` (90 seconds). Both policies keep the existing query variation, eligibility rules and `public-html` eviction tag.
- Topic pages also used unconditional `TempData[...]` in the view, which made `CookieTempDataProvider` emit a clearing `Set-Cookie` on every anonymous visit. Output cache refuses responses with `Set-Cookie`, so topic GET/HEAD never shared. Fixed with the same homepage pattern: read the report/block flash only when the TempData cookie is present (`ForumTopicPageModel.ReadReportStatusMessage` → `ReportStatusMessage`).
- SQLite guards: `MergeModernAsync` with 15 posts / many attachments → exactly 1 read containing `IN (`; no legacy IDs → 0 reads; archive-author page → exactly 3 reads for page sizes 1, 15 and 50. Shared `QueryCounter` covers sync and async readers.
- Production-host web regressions: GET→HEAD and HEAD→GET for topic and archive-author URLs with 0 additional counted repository calls on the second request; empty successful HEAD body; complete subsequent GET body; effective 600s/90s policy durations; cold HEAD 404/301 cases; middleware scope/bypass/restoration tests.
- SqlServerTests: seeds 15 posts with attachments and asserts anonymous topic data load uses ≤ 2 server round trips (LocalDB required; not runnable on this Linux box).

## Files changed

- `src/QueenZone.Web/Caching/PublicOutputCachePolicies.cs` — archive-authors policy, endpoint policy selection, HEAD normalization.
- `src/QueenZone.Web/Infrastructure/QueenZoneWebServiceCollectionExtensions.cs` — register both HTML policies/TTLs.
- `src/QueenZone.Web/Program.cs` — wire HEAD normalization before output cache; `CachePublicHtml()`.
- `src/QueenZone.Web/Pages/Forum/ForumTopicPageModel.cs` — TempData cookie guard + `ReportStatusMessage`.
- `src/QueenZone.Web/Pages/Forum/Topic.cshtml.cs` / `TopicPage.cshtml.cs` — pass `CookieTempDataProviderOptions`.
- `src/QueenZone.Web/Pages/Forum/Topic.cshtml` / `TopicPage.cshtml` — bind flash from model property.
- `tests/QueenZone.Web.Tests/PublicOutputCacheTests.cs` — paired-method cache, TTL, cold HEAD.
- `tests/QueenZone.Web.Tests/PublicOutputCachePoliciesTests.cs` — normalization tests.
- `tests/QueenZone.Web.Tests/ProductionHostFixture.cs` — counting repos + duration observer.
- `tests/QueenZone.Web.Tests/OutputCacheCountingForumRepository.cs` — counting decorators.
- `tests/QueenZone.Web.Tests/OutputCacheExpirationObserver.cs` — observe effective durations.
- `tests/QueenZone.Web.Tests/QueryCounter.cs` — shared reader counter.
- `tests/QueenZone.Web.Tests/EfForumAttachmentRepositoryTests.cs` — 1-read / 0-read merge guards.
- `tests/QueenZone.Web.Tests/EfForumArchiveAuthorRepositoryTests.cs` — 3-read guard across page sizes.
- `tests/QueenZone.SqlServerTests/ModernForumRepositorySqlServerTests.cs` — ≤2-command anonymous topic budget.
- `NOTES-2227.md` — this report.

`LOCK-2227.md` and `ISSUE-2227.md` are scratch inputs and are not committed. Nothing under `.github/workflows` was touched.

## Verification results

Passed (Debug, .NET SDK 10.0.401 on this box; SixLabors license absent so Release ImageSharp gate not used):

- `git diff --check` (on intentional paths)
- `dotnet build tests/QueenZone.Web.Tests/QueenZone.Web.Tests.csproj --configuration Debug`
- `dotnet test ... --filter FullyQualifiedName~PublicOutputCache|...EfForumAttachment...|...EfForumArchiveAuthor...|...ArchiveAuthorPageTests` → **80 passed, 0 failed**

Blocked / unavailable here:

- SqlServerTests `Anonymous_topic_page_uses_at_most_two_commands` → `PlatformNotSupportedException: LocalDB is not supported on this platform` (Linux box).
- Release build without `SIXLABORS_LICENSE_KEY` / Bitwarden `bws` (Debug continues on license warning).
- Production SQL audit AC5 (ops follow-up).

Codex's own session could not run `dotnet` (sandbox PATH/network); the executor installed SDK 10, fixed a Codex compile typo (`RazorPagesEndpointConventionBuilder` → `PageActionEndpointConventionBuilder`), and applied the TempData Set-Cookie fix after diagnostics.

## Open questions and deviations

- Run SqlServerTests on a Windows/LocalDB (or configured SQL) host before merge.
- AC5 remains Gilfoyle ops: verify attachment/proc counts and cache reuse from SQL audit, not App Insights.
- TempData guard is a small product fix beyond Codex's first draft; required for topic pages to participate in output cache at all (homepage already had the same pattern).
- No push / no PR / no GitHub comments, as requested.
