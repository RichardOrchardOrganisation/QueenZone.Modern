# Latest-topic promotion and participation clarity

The cross-board latest-topic feed excludes legacy forum ID **7**, **Queenzone.com**
(`/forum/7/queenzone-com`). Richard explicitly requested this board exclusion on
3 October 2026 after test discussions appeared in homepage promotions. The public
board URL was checked and its heading confirmed before implementation.

The exclusion belongs in `IForumRepository.GetRecentThreadsAsync` and all three
implementations (modern EF, legacy rollback SQL, and in-memory). It applies before
ordering limits (`Take` / `TOP`) so eligible discussions fill the requested feed.
The homepage Forum now card, forum band and Happening now ticker, forum index
recent-thread table, and `/api/v1/forum/recent-threads` share that feed. Installed
mobile clients using the API receive the same rule without a client update.

This is a promotion rule, not a content visibility or moderation action. Board
cards and category listings, direct topic reading and participation, search,
sitemaps, and activity totals retain their existing behavior. No rows are deleted,
classified, edited, or hidden; no migration or production database operation is
required. Website support discussions also stop appearing in the promotional
feed, as requested.

Titles and authors are deliberately not used to identify test content. A genuine
member discussion such as “Test pressings of Queen II” remains eligible outside
board 7. The forum entities have no explicit test/seed-post flag. Their existing
`IsSynthetic` category flag and hidden/validated flags serve import and visibility
rules and must not be repurposed to classify test messages. Test-looking posts
outside board 7 require separate evidence and explicit approval before any further
exclusion; this change does not infer their provenance.

The forum index and board metadata describe browsing and member participation
rather than a read-only forum. The mobile-app page leads with the live iPhone
App Store download and labels Android separately as testing. The iPhone store
listing was publicly verified on 3 October 2026. The existing Android tester URL
redirects signed-out visitors to Google sign-in; tester enrolment and Play Console
release status could not be independently checked without an authorized session.
The existing Android testing steps and destinations are preserved.

Regression coverage includes modern EF/SQLite and in-memory ordering and count
behavior, HTTP homepage/API promotion, forum recent-table exclusion and retained
board/topic routes, and a SQL Server scratch-schema test plus read-only mirror
probe assertions. Browser coverage exercises the homepage-to-app-page path,
platform order and destinations, forum sign-in guidance, and axe checks.
