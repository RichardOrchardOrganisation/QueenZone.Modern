# ADR 0004: Treat Legacy Schema As Import Source

## Status

Superseded by [ADR 0021](0021-legacy-database-is-production.md). The
"import source" framing below no longer holds — the legacy database is the
permanent production database, not a source feeding a separate destination.
The operational description of which tables serve which reads (forum on
`ModernForum*`, other content on legacy tables) is still accurate and is
carried forward informally in ADR 0021.

Amended 2026-07-09: forum public reads now use modern projected tables by default; other content areas may continue reading legacy tables unless performance or safety problems appear.

## Context

The legacy database is valuable because it contains the historical QueenZone content. It is also old, highly procedural, and mixes public content with user accounts, private data, community features, tracking data, and administrative concerns.

Some areas, especially forums, are unlikely to be efficient or pleasant to query directly for a modern public archive.

## Decision

The legacy schema remains an import source and historical reference. It is not required that every public content area move onto modern tables before launch.

The modern application may read directly from legacy tables and stored procedures for public archive content. Introduce modern read-model tables when that improves safety, performance, clarity, or maintainability — especially for large or awkward areas such as forums.

### Current policy

- **Forum archive:** public reads use the imported `ModernForum*` tables by default (`ForumData:UseModernForumReads = true`, `ModernForumRepository`). Legacy forum SQL remains available as a fallback and historical source, not the primary production path.
- **Other public content** (news, articles, biography, discography, photography, and similar): continue reading legacy tables/stored procedures unless we discover performance, privacy, or maintainability problems that justify a modern projection.
- **New editorial workflows** (for example admin news writes and discovery tables): continue using deliberately designed modern tables rather than extending legacy write paths.

## Consequences

Benefits:

- We can launch quickly while still allowing a better long-term shape.
- Forum archive pages are optimized for modern browsing and search via projected tables.
- Private/sensitive fields can be excluded at import time.
- Old IDs and URLs can still be preserved.
- Non-forum content can stay on proven legacy reads without premature schema rewrites.

Tradeoffs:

- Requires import jobs and repeatable migration scripts.
- Data correctness needs validation reports.
- There may be a period where both legacy and modern tables exist.
- Production must keep modern forum import/read-path scripts and indexes healthy (`docs/sql/006-modern-forum-read-path.sql` and related import tooling).
