# Legacy URL Notes

The legacy site has been offline for years, so old Web Forms URL preservation is not a launch requirement.

Keep these notes only as historical reference when identifying content relationships and legacy IDs. New public pages should use clean, stable, search-friendly canonical URLs.

## Historical URL Shapes

| Legacy URL Shape | Modern Canonical Shape | Notes |
| --- | --- | --- |
| `/` | `/` | Homepage. |
| `/default.aspx` | `/` | Legacy homepage. |
| `/news.aspx` | `/news` | News archive. |
| `/process/news_view.aspx?news_id={id}` | `/news/{id}/{slug}` | News detail. |
| `/articles.aspx` | `/articles` | Articles archive. |
| `/article_category.aspx?q={id}` | `/articles/category/{id}/{slug}` | Confirm query parameter. |
| `/process/article_show.aspx?q={id}` | `/articles/{id}/{slug}` | Article detail. |
| `/biography/default.aspx` | `/biography` | Biography landing. |
| `/process/bio_view.aspx?q={id}` | `/biography/{id}/{slug}` | Biography detail. |
| `/faq.aspx` | `/faq` | FAQ landing. |
| `/faq_category_view.aspx?q={id}` | `/faq/category/{id}/{slug}` | FAQ category. |
| `/process/faq_view.aspx?q={id}` | `/faq/{id}/{slug}` | FAQ detail. |
| `/pictures.aspx` | `/pictures` | Picture landing. |
| `/picture-category.aspx?q={id}` | `/pictures/category/{id}/{slug}` | Picture category. |
| `/process/picture_view.aspx?Q={id}` | `/pictures/{id}/{slug}` | Picture detail. |
| `/discography/album_view.aspx?q={id}` | `/discography/albums/{id}/{slug}` | Album detail. |
| `/discography/song_view.aspx?q={id}` | `/discography/songs/{id}/{slug}` | Song detail. |
| `/forums/` | `/archive/forums` | Forum archive landing. |
| `/forums/default.aspx` | `/archive/forums` | Forum archive landing. |
| `/forums/forum_view.aspx?q={id}` | `/archive/forums/{id}/{slug}` | Forum category. |
| `/forums/forum_topic_view.aspx?q={id}` | `/archive/forums/topics/{id}/{slug}` | Forum topic. Also `/queenzone/forumnew/forum_topic_view.aspx?Q={id}`. |
| `/profile.aspx?q={id}` | `/archive/members/{id}` | Only if a public member archive is deliberately enabled later. |

## Links Inside Archived Content

QueenZone no longer owns `queenzone.com`, so requests for old URLs never reach this app. Archived forum posts, news, articles and biography text still contain about 6,000 links to the old site, though. `LegacyQueenZoneLinks` (`src/QueenZone.Web/Content/LegacyQueenZoneLinks.cs`) rewrites them when a body is formatted for display. The stored text keeps the original URL.

| Old link (any of `queenzone.com`, `queenzone.org`, with or without `www.`) | Rendered href |
| --- | --- |
| `.../forumnew/forum_topic_view.aspx?Q={id}` (also `&page=`, `Q_FORUM_ID=…&Q={id}`) | `/forum/goto/{id}` |
| `/forums/forum_topic_view.aspx?Q={id}` | `/forum/goto/{id}` |
| `/forums/{id}/{slug}.aspx` | `/forum/goto/{id}` |
| `.../forum_view.aspx?Q={forumId}` | `/forum/{forumId}/forum` (redirects to the canonical slug) |
| `.../forumnew/default.aspx`, `/forums/` | `/forum` |
| `.../news_view.aspx?news_id={id}` | `/news/{id}/news` (redirects to the canonical slug) |
| `.../news.aspx` | `/news` |

`Q` is a legacy `Q_FORUM_TOPIC_ID`, which can be a topic starter or a reply. `/forum/goto/{id}` looks it up (`IForumRepository.FindLegacyPostAsync`) and redirects to the topic page that holds it, with `#post-{id}` for replies. Unknown or hidden IDs return 404. The old `page` value is ignored because the old page size differs from the modern 15 posts per page.

## Implementation Notes

- Do not implement legacy URL redirects by default. Rewriting links inside archived content (above) is the exception.
- Use legacy IDs where they help stable content identity.
- Generate readable slugs from titles.
- Return normal 404 responses for old Web Forms paths unless a specific redirect is later justified.
- Keep canonical URLs documented per content type.
