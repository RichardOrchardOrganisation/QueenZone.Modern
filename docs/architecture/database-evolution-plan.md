# Database Evolution Plan

## Position

The legacy `MAIN2_DB` schema (`queenzone-db`) is the permanent production database — see [ADR 0021](../decisions/0021-legacy-database-is-production.md), which superseded the earlier "import source" framing (ADR 0004). It is not a source feeding a separate destination database; modern EF-managed tables already live in this same database alongside the legacy tables.

QueenZone Modern may keep reading most public archive content from legacy tables. Modern projected tables are introduced when a content area needs better performance, privacy boundaries, or maintainability — not as a blanket rewrite of every legacy read.

## Current State

- **Forum archive:** migrated. Public `/forum` routes read `ModernForum*` tables by default via `ModernForumRepository` (`ForumData:UseModernForumReads = true`). See `docs/sql/006-modern-forum-read-path.sql` and `docs/performance/forum-read-benchmark-2026-06-29.md`.
- **Other public content:** continue legacy table/stored-procedure reads unless we discover performance or safety problems.
- **Editorial/admin workflows:** use deliberately designed modern tables (news admin columns, discovery, audit) rather than extending legacy write paths.

## Why Forum Needed Modern Tables

Forum was the main pressure point:

- Topic and reply data lived in large legacy `Q_FORUM_TOPIC_T` structures.
- Parent/child relationships were expensive to page for modern archive routes.
- Search and archive browsing needed indexes that suit modern URLs.
- User/account fields sat close to public post data.
- Attachment, moderation, and tracker-adjacent data needed careful separation.

## Recommended Pattern

Use an import/project pattern when a content area needs it:

```mermaid
flowchart LR
  Legacy["Legacy MAIN2_DB"] --> Importer["QueenZone.Import"]
  Importer --> Modern["Modern read model tables"]
  Modern --> Web["QueenZone.Web"]
  Legacy --> DirectReads["Direct legacy reads for other content"]
  DirectReads --> Web
  Legacy --> Audit["Migration reports"]
```

Forum already follows the modern-table path. Other archive areas may stay on direct legacy reads until evidence says otherwise.

**2026-07-27 evaluation (issue #334):** live timings for `NEWS_T` latest-row CTE and other public legacy reads are in `docs/performance/legacy-to-modern-eval-2026-07-27.md`. Conclusion: do **not** forum-style modernize news for performance (server page SQL ~4–33 ms; zero duplicate `NEWS_ID` groups today). Revisit only for product dual-source live news or if measurements regress.

## Candidate Modern Tables

Names are placeholders.

### Content

- `ContentItem`
- `ContentCategory`
- `ContentRevision`
- `LegacyContentSource`

### Redirects

- `LegacyRedirect`

Fields:

- `Id`
- `OldPath`
- `OldQuery`
- `TargetPath`
- `ContentType`
- `LegacyId`
- `StatusCode`

### Media

- `MediaAsset`
- `MediaVariant`
- `LegacyMediaSource`

### Forum Archive

Shipped as imported `ModernForum*` tables (see `ModernForumRepository` and `docs/sql/006-modern-forum-read-path.sql`). Placeholder names below map to that family:

- `ForumCategory`
- `ForumThread`
- `ForumPost`
- `ForumAuthorSnapshot`
- `ForumAttachment`
- `ForumModerationFlag`

### Search

- `SearchDocument`

Fields:

- `Id`
- `ContentType`
- `ContentId`
- `Title`
- `Body`
- `AuthorDisplayName`
- `PublishedAt`
- `Url`

## Migration Rules

- Legacy tables may be altered through reviewed, repeatable migrations like any other production schema (ADR 0021) — this is no longer a frozen source to read around.
- Store imported/projected modern data in separate tables within the same production database (`queenzone-db`); there is no separate destination database.
- Keep `LegacyId` fields on modern records.
- Keep import runs repeatable.
- Generate reports for skipped, hidden, malformed, or unsafe records.
- Do not import private messages, emails, password fields, IP addresses, or private profile fields into public read models.
- Ordinary production-change discipline still applies to legacy tables: back up before destructive schema changes, avoid breaking in-flight application code, keep migrations reversible where practical.

## Forum Archive Status

Completed for the public browse path:

1. Forum/thread/post data imported into `ModernForum*` tables.
2. Public category, topic, and post pages render from modern tables by default.
3. Read-path procedures and covering indexes are documented and benchmarked.
4. Legacy forum SQL remains available as fallback/historical source via `ForumData:UseModernForumReads`.

Remaining forum follow-ups (not blockers for the modern-table decision):

- Keep import/reconciliation and `ModernForum_RefreshReadStats` healthy after future imports.
- Decide full-text search hosting (Azure SQL full-text vs Azure AI Search).
- Confirm profile-link and takedown/moderation policy for archive authors.

## Open Questions

- Should the new project keep legacy and modern tables in one Azure SQL database, or use separate databases?
- Should forum archive content be indexed in Azure SQL full-text or Azure AI Search?
- Should old user profile links be preserved, anonymized, or disabled?
- How should takedown/moderation requests be handled after launch?
- Which non-forum content areas, if any, show enough performance or privacy pressure to justify their own modern projections?
