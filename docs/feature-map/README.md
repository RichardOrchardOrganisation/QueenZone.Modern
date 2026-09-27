# Feature map

Machine-readable map of every mobile screen and public or member web page.
Agents and the verify skills read these JSON files; do not hand-edit this index.
Regenerate with `node scripts/check-feature-map.mjs --write`.

## Mobile

### home

- `mobile.home.home` — Home (HomeStack/Home)
- `mobile.home.quote` — Quote (HomeStack/Quote)

### news

- `mobile.news.index` — News index (NewsStack/NewsIndex)
- `mobile.news.story` — News story (HomeStack/Story, NewsStack/Story)
- `mobile.news.suggest` — Suggest news (HomeStack/SuggestNews)

### photos

- `mobile.photos.category` — Photo collection (PhotosStack/PhotoCategory)
- `mobile.photos.index` — Photography index (PhotosStack/PhotoIndex)
- `mobile.photos.submit` — Submit a photo (PhotosStack/PhotoSubmit)
- `mobile.photos.viewer` — Photo viewer (PhotosStack/PhotoViewer)

### archive

- `mobile.archive.about` — About the archive (ArchiveStack/AboutArchive)
- `mobile.archive.album` — Album (ArchiveStack/Album)
- `mobile.archive.articles` — Articles index (ArchiveStack/Articles)
- `mobile.archive.biography` — Biography (ArchiveStack/Biography)
- `mobile.archive.biographyChapter` — Biography chapter (ArchiveStack/BiographyChapter)
- `mobile.archive.discography` — Discography (ArchiveStack/Discography)
- `mobile.archive.fanPerformanceDetail` — Fan performance (ArchiveStack/FanPerformanceDetail)
- `mobile.archive.fanPerformanceDownloads` — Fan performance downloads (ArchiveStack/FanPerformanceDownloads)
- `mobile.archive.fanPerformances` — Fan performances (ArchiveStack/FanPerformances)
- `mobile.archive.fanPerformanceSubmit` — Submit a fan performance (ArchiveStack/FanPerformanceSubmit)
- `mobile.archive.freddieTribute` — Freddie Tribute (ArchiveStack/FreddieTribute)
- `mobile.archive.hub` — Archive hub (ArchiveStack/ArchiveHub)
- `mobile.archive.quizLeaderboard` — Quiz leaderboard (ArchiveStack/QuizLeaderboard)
- `mobile.archive.quizList` — Quiz list (ArchiveStack/QuizList)
- `mobile.archive.quizPlay` — Quiz play (ArchiveStack/QuizPlay)
- `mobile.archive.quizSprint` — Quiz Sprint (ArchiveStack/QuizSprint)
- `mobile.archive.quizSprintLeaderboard` — Quiz Sprint leaderboard (ArchiveStack/QuizSprintLeaderboard)
- `mobile.archive.search` — Search (HomeStack/Search, NewsStack/Search, PhotosStack/Search, ArchiveStack/Search, ForumStack/Search)
- `mobile.archive.story` — Archive article (ArchiveStack/Story)
- `mobile.archive.timeline` — Timeline (ArchiveStack/Timeline)
- `mobile.archive.timelineEvent` — Timeline event (ArchiveStack/TimelineEvent)
- `mobile.archive.trivia` — Trivia (ArchiveStack/Trivia)

### forum

- `mobile.forum.category` — Forum board (ForumStack/Category)
- `mobile.forum.composer` — Forum composer (ForumStack/Composer)
- `mobile.forum.index` — Forum index (ForumStack/ForumIndex)
- `mobile.forum.report` — Report a post (ForumStack/ForumReport)
- `mobile.forum.thread` — Forum thread (ForumStack/Thread)

### account

- `mobile.account.analytics` — Analytics preferences (HomeStack/AnalyticsSettings)
- `mobile.account.appearance` — Appearance (HomeStack/Appearance)
- `mobile.account.contact` — Contact (HomeStack/Contact)
- `mobile.account.delete` — Delete account (HomeStack/DeleteAccount)
- `mobile.account.profile` — Profile (HomeStack/Profile)
- `mobile.account.saved` — Library (HomeStack/SavedList)
- `mobile.account.settings` — Settings (HomeStack/Settings)
- `mobile.account.submissions` — My submissions (HomeStack/MySubmissions)

### messages

- `mobile.messages.archived` — Archived messages (HomeStack/Archived)
- `mobile.messages.compose` — New message (HomeStack/ComposeMessage)
- `mobile.messages.conversation` — Conversation (HomeStack/Conversation)
- `mobile.messages.inbox` — Messages inbox (HomeStack/Inbox)

### auth

- `mobile.auth.signIn` — Sign in (RootStack/SignIn)

## Web

### home

- `web.home.index` — Homepage (Pages/Index.cshtml — /)

### news

- `web.news.detail` — News article (Pages/News/Detail.cshtml — /news/{id}/{slug})
- `web.news.index` — News archive (Pages/News/Index.cshtml — /news)
- `web.news.page` — News archive page (Pages/News/Page.cshtml — /news/page/{pageNumber})
- `web.news.search` — News search (Pages/News/Search.cshtml — /news/search)
- `web.news.submit` — Suggest news (Pages/Submit/News.cshtml — /submit/news)
- `web.news.submitConfirmation` — News suggestion confirmation (Pages/Submit/NewsConfirmation.cshtml — /submit/news/confirmation)

### photos

- `web.photos.category` — Photo collection (Pages/Photography/Category.cshtml — /photography/{slug})
- `web.photos.categoryPage` — Photo collection page (Pages/Photography/CategoryPage.cshtml — /photography/{slug}/page/{pageNumber})
- `web.photos.detail` — Photograph (Pages/Photography/Detail.cshtml — /photography/{slug}/{picId})
- `web.photos.index` — Photography (Pages/Photography/Index.cshtml — /photography)
- `web.photos.submit` — Submit a photo (Pages/Submit/Photo.cshtml — /submit/photo)
- `web.photos.submitConfirmation` — Photo submission confirmation (Pages/Submit/PhotoConfirmation.cshtml — /submit/photo/confirmation/{id})

### archive

- `web.archive.album` — Album (Pages/Discography/Album.cshtml — /discography/albums/{id}/{slug})
- `web.archive.articleDetail` — Archive article (Pages/Articles/Detail.cshtml — /articles/{id}/{slug})
- `web.archive.articles` — Articles (Pages/Articles/Index.cshtml — /articles)
- `web.archive.articleSample` — Sample article holding page (Pages/Article.cshtml — /article)
- `web.archive.articlesPage` — Articles page (Pages/Articles/Page.cshtml — /articles/page/{pageNumber})
- `web.archive.biography` — Biography (Pages/Biography/Index.cshtml — /biography)
- `web.archive.biographyDetail` — Biography chapter (Pages/Biography/Detail.cshtml — /biography/{id}/{slug})
- `web.archive.communityArticle` — Community article (Pages/Articles/CommunityDetail.cshtml — /articles/{slug})
- `web.archive.discography` — Discography (Pages/Discography/Index.cshtml — /discography)
- `web.archive.fanPerformanceReport` — Report a fan performance (Pages/FanPerformances/Report.cshtml — /fan-performances/{id}/report)
- `web.archive.fanPerformances` — Fan performances (Pages/FanPerformances/Index.cshtml — /fan-performances)
- `web.archive.fanPerformancesPage` — Fan performances page (Pages/FanPerformances/Page.cshtml — /fan-performances/page/{pageNumber})
- `web.archive.following` — Following feed (Pages/Following.cshtml — /following)
- `web.archive.freddieTribute` — Freddie Tribute (Pages/FreddieTribute/Index.cshtml — /freddie-mercury-tribute)
- `web.archive.freddieTributePage` — Freddie Tribute page (Pages/FreddieTribute/Page.cshtml — /freddie-mercury-tribute/page/{pageNumber})
- `web.archive.quizLeaderboard` — Quiz leaderboard (Pages/Quizzes/Leaderboard.cshtml — /quizzes/leaderboard)
- `web.archive.quizPlay` — Quiz play (Pages/Quizzes/Play.cshtml — /quizzes/{id})
- `web.archive.quizSprint` — Quiz Sprint (Pages/Quizzes/Sprint.cshtml — /quizzes/sprint)
- `web.archive.quizzes` — Quizzes (Pages/Quizzes/Index.cshtml — /quizzes)
- `web.archive.rareDiscography` — Rare discography (Pages/Discography/RareDiscography.cshtml — /discography/rare-discography)
- `web.archive.search` — Search the archive (Pages/Search.cshtml — /search)
- `web.archive.submitArticle` — Submit an article (Pages/Submit/Article.cshtml — /submit/article/{id?})
- `web.archive.submitArticleConfirmation` — Article submission confirmation (Pages/Submit/ArticleConfirmation.cshtml — /submit/article/confirmation/{id})
- `web.archive.submitFanPerformance` — Submit a fan performance (Pages/Submit/FanPerformance.cshtml — /submit/fan-performance)
- `web.archive.submitFanPerformanceConfirmation` — Fan performance confirmation (Pages/Submit/FanPerformanceConfirmation.cshtml — /submit/fan-performance/confirmation/{id})
- `web.archive.submitQuiz` — Submit a quiz question (Pages/Submit/QuizQuestion.cshtml — /submit/quiz-question)
- `web.archive.submitQuizConfirmation` — Quiz question confirmation (Pages/Submit/QuizQuestionConfirmation.cshtml — /submit/quiz-question/confirmation/{id})
- `web.archive.submitTrivia` — Submit trivia (Pages/Submit/Trivia.cshtml — /submit/trivia)
- `web.archive.submitTriviaConfirmation` — Trivia submission confirmation (Pages/Submit/TriviaConfirmation.cshtml — /submit/trivia/confirmation/{id})
- `web.archive.timeline` — Timeline (Pages/Timeline/Index.cshtml — /timeline)
- `web.archive.trivia` — Trivia (Pages/Trivia/Index.cshtml — /trivia)

### forum

- `web.forum.archiveAuthor` — Archive author (Pages/Forum/ArchiveAuthor.cshtml — /forum/archive-authors/{legacyUserId})
- `web.forum.block` — Block a member (Pages/Forum/Block.cshtml — /forum/post/{postId}/block)
- `web.forum.category` — Forum board (Pages/Forum/Category.cshtml — /forum/{id}/{slug})
- `web.forum.categoryPage` — Forum board page (Pages/Forum/CategoryPage.cshtml — /forum/{id}/{slug}/page/{pageNumber})
- `web.forum.editPost` — Edit post (Pages/Forum/EditPost.cshtml — /forum/post/{postId}/edit)
- `web.forum.hideAuthor` — Hide author (Pages/Forum/HideAuthor.cshtml — /forum/post/{postId}/hide-author)
- `web.forum.index` — Forum (Pages/Forum.cshtml — /forum)
- `web.forum.newThread` — New thread (Pages/Forum/NewThread.cshtml — /forum/c/{categorySlug}/new-thread)
- `web.forum.report` — Report a post (Pages/Forum/Report.cshtml — /forum/post/{postId}/report)
- `web.forum.topic` — Forum topic (Pages/Forum/Topic.cshtml — /forum/topic/{topicId}/{slug})
- `web.forum.topicPage` — Forum topic page (Pages/Forum/TopicPage.cshtml — /forum/topic/{topicId}/{slug}/page/{pageNumber})

### account

- `web.account.dataDeletion` — Data deletion (Pages/DataDeletion.cshtml — /data-deletion)
- `web.account.delete` — Delete account (Pages/Account/Delete.cshtml — /account/delete)
- `web.account.deletionRequested` — Deletion requested (Pages/Account/DeletionRequested.cshtml — /account/deletion-requested)
- `web.account.deletionStatus` — Deletion status (Pages/Account/DeletionStatus.cshtml — /account/deletion-status)
- `web.account.profile` — Member profile (Pages/Members/Profile.cshtml — /members/{memberId})
- `web.account.settings` — Account settings (Pages/Account/Settings.cshtml — /account/settings)
- `web.account.submissions` — My submissions (Pages/Account/MySubmissions.cshtml — /account/my-submissions)

### messages

- `web.messages.archived` — Archived messages (Pages/Messages/Archived.cshtml — /messages/archived)
- `web.messages.compose` — Compose message (Pages/Messages/Compose.cshtml — /messages/compose)
- `web.messages.conversation` — Conversation (Pages/Messages/Conversation.cshtml — /messages/{conversationId})
- `web.messages.inbox` — Messages (Pages/Messages/Index.cshtml — /messages)

### auth

- `web.auth.externalLogin` — External login (Pages/Account/ExternalLogin.cshtml — /account/external-login)
- `web.auth.externalLoginCallback` — External login callback (Pages/Account/ExternalLoginCallback.cshtml — /account/external-login-callback)
- `web.auth.linkExternalLogin` — Link external login (Pages/Account/LinkExternalLogin.cshtml — /account/link-external-login)
- `web.auth.login` — Sign in (Pages/Account/Login.cshtml — /account/login)
- `web.auth.logout` — Sign out (Pages/Account/Logout.cshtml — /account/logout)

### static

- `web.static.about` — About (Pages/About.cshtml — /about)
- `web.static.contact` — Contact (Pages/Help/Index.cshtml — /contact)
- `web.static.contactConfirmation` — Contact confirmation (Pages/Help/Confirmation.cshtml — /contact/confirmation)
- `web.static.error` — Error (Pages/Error.cshtml — /error/{statusCode?})
- `web.static.helpRedirect` — Legacy help redirect (Pages/Help/LegacyRedirect.cshtml — /help/{*path})
- `web.static.links` — Links (Pages/Links.cshtml — /links)
- `web.static.mobileApps` — Mobile apps (Pages/MobileApps.cshtml — /mobile-apps)
- `web.static.notFound` — Not found (Pages/NotFound.cshtml — /404)
- `web.static.privacy` — Privacy (Pages/Privacy.cshtml — /privacy)
- `web.static.terms` — Terms (Pages/Terms.cshtml — /terms)

## Excluded

- `Pages/Admin/**` — Acceptance criteria cover public and member pages only. Admin editorial screens are out of scope for this map.
