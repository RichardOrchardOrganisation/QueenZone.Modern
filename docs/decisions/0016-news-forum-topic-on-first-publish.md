# ADR 0016: News-Forum Topic On First Article Publish

## Status

Accepted.

## Context

Published QueenZone news articles need a public discussion without inventing a second comments system. The forum already has topics, replies, Watch, and `forumReply` push ([#735](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/735) / [#759](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/759)). [ADR 0004](0004-legacy-schema-is-import-source.md) keeps news on legacy `NEWS_T` and does not project it onto modern tables.

[ADR 0005](0005-admin-news-publishing.md) already writes `NEWS_T` on publish. News push uses the same unpublished → published `firstPublish` gate and is fail-open: the article stays up if dispatch throws.

Open questions from [#1036](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1036) were: which board, which author, how to persist the article ↔ topic link, what detail/list fields to expose, and whether a failed topic create should fail publish.

## Decision

### 1. First publish only; no backfill; unpublish keeps the topic

`AdminNewsWriteService.PublishAsync` creates a forum topic only on the same `firstPublish` gate as news push. Republish and edit are no-ops. Existing articles are not backfilled. Unpublish does not delete the topic or clear the stored link.

### 2. Persist `NEWS_T.FORUM_TOPIC_ID` as an ADR 0005 extension column

`ForumTopicId` is a nullable ADR 0005-style extension column on legacy `NEWS_T` (`FORUM_TOPIC_ID`) plus a unique filtered index when the column is set. The article owns the 1:1. If the column is already set, topic create is a no-op. Do not project news onto modern tables ([ADR 0004](0004-legacy-schema-is-import-source.md)).

### 3. News board by slug first, then name; never The Music

Resolve the category by stable slug `news` first, then by case-insensitive name `News`. If missing, idempotent ensure-create a board named `News`. Do not hardcode a category id and never use The Music.

### 4. System member QueenZone; trusted create path

The opening post is authored by a configured, durable system member with display name `QueenZone` (`NewsForum` options; created if missing), not the Entra editor. Creation still goes through `ForumPostWriteService` sanitization (not raw SQL). Rate-limit and new-account+link auto-spam are bypassed on this trusted path only, because the opening post always contains a URL. Member replies stay on the existing Watch / `forumReply` path. Creating the topic must not send an extra news push.

### 5. Opening post is excerpt plus one public-article link paragraph

Title is the article title. Body is the article's existing excerpt, capped at ~400 characters, plus one link paragraph to `https://www.queenzone.org` and the article detail path. Not the full article. No images. No poll.

### 6. Fail-open after `NEWS_T` write

Order: write `NEWS_T` → ensure News category → `CreateTopic` → persist `ForumTopicId`. Topic create is fail-open like push: the same try/catch + `LogWarning` shape as `NotifyNewsPublishedAsync`, and the article stays published. Repair is a follow-up, not part of this change.

### 7. Read fields for later UI stories

Detail (`NewsDetailDto` and the website news model): `topicId`, `discussionReplyCount`, `discussionPreview` of the last N replies (not the opening post). N = 2, or 1 if only one reply exists. Listings: `topicId` and `replyCount` only, batched, no bodies, no N+1. Null `topicId` means clients omit the discussion block. Website and mobile UI consume these fields in later stories.

## Consequences

Benefits:

- One discussion surface: the forum topic.
- Same first-publish and fail-open posture as news push.
- Listings stay cheap: reply counts are batched without post bodies.

Tradeoffs:

- Articles published before this change have no topic until a later repair/backfill story.
- A failed topic create leaves a published article without `ForumTopicId` until repaired.
- `NEWS_T` gains another nullable editorial column.

## Related

- [#1035](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1035) — Epic: Open a News-forum topic for every published article
- [#1036](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1036) — Create a News-forum topic on first article publish and store the link
- [#1037](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1037) / [#1038](https://github.com/RichardOrchardOrganisation/QueenZone.Modern/issues/1038) — Website / mobile discussion UI
- [ADR 0004](0004-legacy-schema-is-import-source.md) — do not project news
- [ADR 0005](0005-admin-news-publishing.md) — admin news publishing
- [ADR 0014](0014-push-notification-transport-and-dispatch.md) — fail-open push dispatch
