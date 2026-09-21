namespace QueenZone.Web;

/// <summary>
/// List-card shape for <c>/api/v1/content/news</c>. Deliberately decoupled from
/// <see cref="NewsArchiveItem"/> so changes to the Razor Pages view model do not
/// silently change the mobile JSON contract.
/// <c>ImageUrl</c> / <c>ThumbnailUrl</c> are resolved public URLs (or
/// <see langword="null"/> when the article has no image or the reference is a
/// gallery pick). The database stores blob keys only — never image bytes.
/// </summary>
public sealed record NewsListItemDto(
    int Id,
    string Title,
    string Excerpt,
    DateTime PublishedAt,
    string DetailPath,
    string? ImageUrl = null,
    string? ThumbnailUrl = null,
    int? TopicId = null,
    int? ReplyCount = null);

/// <summary>
/// Last-N forum reply preview for news detail. Not the opening post.
/// </summary>
public sealed record NewsDiscussionPreviewDto(
    string AuthorDisplayName,
    DateTime PostedAt,
    string Excerpt);

/// <summary>
/// Detail shape for <c>/api/v1/content/news/{id}</c>.
/// <c>Body</c> is sanitized HTML suitable for display (same allowlist as
/// <see cref="NewsArticleContent.FormatBody"/>): basic formatting, links, and UGC images.
/// Plain-text legacy bodies are HTML-encoded with line breaks and auto-linked URLs.
/// <c>ImageUrl</c> / <c>ThumbnailUrl</c> follow the same resolved-URL contract as
/// <see cref="NewsListItemDto"/>.
/// </summary>
public sealed record NewsDetailDto(
    int Id,
    string Title,
    string Excerpt,
    string Body,
    DateTime PublishedAt,
    string? SourceUrl,
    string DetailPath,
    string? ImageUrl = null,
    string? ThumbnailUrl = null,
    int? TopicId = null,
    int? DiscussionReplyCount = null,
    IReadOnlyList<NewsDiscussionPreviewDto>? DiscussionPreview = null);

/// <summary>
/// Earliest/latest published years for <c>/api/v1/content/news/years</c>. Backs the mobile
/// year-rail scrubber's tick marks (issue #886); both are <see langword="null"/> when the
/// archive has no published articles.
/// </summary>
public sealed record NewsYearRangeDto(int? MinYear, int? MaxYear);

/// <summary>
/// List-card shape for <c>/api/v1/content/articles</c>. Long-form editorial archive
/// only (<see cref="QueenZone.Data.ArticleItem"/>) — not news and not community
/// submissions. No images or forum topic.
/// </summary>
public sealed record ArticleListItemDto(
    int Id,
    string Title,
    string Excerpt,
    DateTime PublishedAt,
    string DetailPath,
    string? CategoryName = null);

/// <summary>
/// Detail shape for <c>/api/v1/content/articles/{id}</c>.
/// <c>Body</c> is sanitized HTML via <see cref="ArticleContent.FormatBody"/>.
/// <c>Source</c> is a string: a public http(s) URL or plain attribution text.
/// </summary>
public sealed record ArticleDetailDto(
    int Id,
    string Title,
    string Excerpt,
    string Body,
    DateTime PublishedAt,
    string? Source,
    string? CategoryName,
    string DetailPath);

/// <summary>
/// List-card shape for <c>/api/v1/content/timeline</c> and
/// <c>/api/v1/content/timeline/{id}</c>. The website still has no single-event page;
/// the by-id route is the mobile On This Day widget destination.
/// </summary>
public sealed record TimelineEventDto(
    int Id,
    string Title,
    string Summary,
    DateTime EventDate,
    string FormattedDate,
    string Category,
    string CategoryLabel,
    string? SourceUrl);

/// <summary>
/// List-card shape for <c>/api/v1/content/biography</c>.
/// </summary>
public sealed record BiographyChapterListItemDto(
    int Id,
    string Title,
    string Summary,
    int DisplaySequence,
    string DetailPath);

/// <summary>
/// Detail shape for <c>/api/v1/content/biography/{id}</c>, including adjacent-chapter
/// links so the app can render prev/next navigation without a second round trip.
/// </summary>
public sealed record BiographyChapterDetailDto(
    int Id,
    string Title,
    string Summary,
    string Body,
    int DisplaySequence,
    string DetailPath,
    BiographyChapterNavDto? Previous,
    BiographyChapterNavDto? Next);

/// <summary>
/// Minimal reference to an adjacent chapter for prev/next navigation.
/// </summary>
public sealed record BiographyChapterNavDto(int Id, string Title, string DetailPath);

/// <summary>
/// List-card shape for <c>/api/v1/content/discography</c>.
/// </summary>
public sealed record AlbumListItemDto(
    int AlbumId,
    string Name,
    int? ReleaseYear,
    string? ThumbnailUrl,
    string DetailPath);

/// <summary>
/// Detail shape for <c>/api/v1/content/discography/{id}</c>, including the track list.
/// </summary>
public sealed record AlbumDetailDto(
    int AlbumId,
    string Name,
    int? ReleaseYear,
    string ArtistName,
    string? GeneralNotes,
    string? CoverUrl,
    string DetailPath,
    IReadOnlyList<AlbumSongDto> Songs);

/// <summary>
/// A single track within an <see cref="AlbumDetailDto"/>.
/// </summary>
public sealed record AlbumSongDto(int SongId, string Title, bool IsSingle, string? Lyrics, string? Notes);

/// <summary>
/// List-card shape for <c>/api/v1/content/freddietribute</c>. No detail endpoint: the
/// website has no single-tribute page, only the paged tribute archive.
/// </summary>
public sealed record FreddieTributeDto(
    int Id,
    string Name,
    string Thought,
    string? Country,
    string DateText,
    string? TimeText);

/// <summary>
/// Shape for <c>/api/v1/content/live-activity</c>. No presence/heartbeat tracking exists,
/// so this deliberately carries only the honestly-computable count of forum replies posted
/// today, not a "members reading" figure.
/// </summary>
public sealed record LiveActivitySummaryDto(int NewForumRepliesToday);

/// <summary>
/// Shape for <c>/api/v1/content/quotes/random</c> and <c>/api/v1/content/quotes/{id}</c>.
/// <see cref="Context"/> is the existing <c>QUEEN_QUOTE_T.CONTEXT</c> column (nullable).
/// </summary>
public sealed record QuoteDto(int Id, string Text, string WhoSaid, string? Context);

/// <summary>
/// Shape for <c>/api/v1/content/trivia/random</c>. Optional <see cref="Category"/>,
/// <see cref="Difficulty"/>, and <see cref="Source"/> are omitted when blank.
/// </summary>
public sealed record TriviaDto(
    int Id,
    string Text,
    string? Category = null,
    string? Difficulty = null,
    string? Source = null);

/// <summary>
/// Shape for <c>GET /api/v1/content/home-poll</c> and the website Index block.
/// JSON <c>null</c> when no poll is current. Counts and percentages are public.
/// </summary>
public sealed record HomePollDto(
    Guid Id,
    string Question,
    IReadOnlyList<HomePollOptionDto> Options,
    int TotalVotes,
    bool IsClosed,
    bool ViewerHasVoted,
    Guid? SelectedOptionId);

public sealed record HomePollOptionDto(Guid Id, string Text, int Count, double Percentage);

public sealed record HomePollVoteRequestDto(Guid? OptionId);

/// <summary>Shape for <c>GET /api/v1/content/quizzes</c> list items.</summary>
public sealed record QuizListItemDto(Guid Id, string Title, string? Description, int QuestionCount);

/// <summary>An answer option shaped for play — no correct-answer flag.</summary>
public sealed record QuizOptionDto(Guid Id, string Text);

public sealed record QuizQuestionDto(Guid Id, string Text, int Points, IReadOnlyList<QuizOptionDto> Options);

/// <summary>
/// Shape for <c>GET /api/v1/content/quizzes/{id}</c>. Options only, no correct-answer flag —
/// the correct option is never sent to the client before submit.
/// </summary>
public sealed record QuizDetailDto(Guid Id, string Title, string? Description, IReadOnlyList<QuizQuestionDto> Questions);

public sealed record QuizAnswerSubmissionDto(Guid QuestionId, Guid? SelectedOptionId);

/// <summary>Request body for <c>POST /api/v1/content/quizzes/{id}/attempts</c>.</summary>
public sealed record QuizSubmitRequestDto(IReadOnlyList<QuizAnswerSubmissionDto>? Answers);

public sealed record QuizAnswerResultDto(
    Guid QuestionId,
    string QuestionText,
    Guid? SelectedOptionId,
    string? SelectedOptionText,
    Guid CorrectOptionId,
    string CorrectOptionText,
    bool IsCorrect,
    int PointsAwarded);

/// <summary>
/// Result of <c>POST /api/v1/content/quizzes/{id}/attempts</c>. Scoring is computed
/// server-side; this is the only place the correct answers are ever revealed to the client.
/// </summary>
public sealed record QuizResultDto(
    Guid QuizId,
    string QuizTitle,
    int Score,
    int MaxScore,
    int CorrectCount,
    int QuestionCount,
    bool Recorded,
    IReadOnlyList<QuizAnswerResultDto> Answers);

public sealed record QuizLeaderboardEntryDto(int Rank, string DisplayName, int Score, int AttemptCount);

/// <summary>Shape for <c>GET /api/v1/content/quizzes/leaderboard</c>.</summary>
public sealed record QuizLeaderboardDto(
    IReadOnlyList<QuizLeaderboardEntryDto> Top,
    QuizLeaderboardEntryDto? Viewer,
    int TotalMembers);

public sealed record SprintOptionDto(Guid Id, string Text);

public sealed record SprintQuestionDto(Guid Id, string Text, IReadOnlyList<SprintOptionDto> Options);

/// <summary>
/// Shape for <c>POST /api/v1/content/quizzes/sprint/start</c>. Options carry no correct-answer flag;
/// <c>Ticket</c> is an opaque signed token to send back on finish. Times are Unix milliseconds (UTC).
/// </summary>
public sealed record SprintRoundDto(
    string Ticket,
    long ServerNowUnixMilliseconds,
    long ExpiresAtUnixMilliseconds,
    int DurationSeconds,
    IReadOnlyList<SprintQuestionDto> Questions);

/// <summary>Request body for <c>POST /api/v1/content/quizzes/sprint/answer</c>.</summary>
public sealed record SprintAnswerRequestDto(string? Ticket, Guid QuestionId, Guid OptionId);

/// <summary>Whether the picked option was right, and which one was, for per-answer feedback.</summary>
public sealed record SprintAnswerResultDto(bool IsCorrect, Guid CorrectOptionId);

public sealed record SprintFinishRequestDto(string? Ticket, IReadOnlyList<QuizAnswerSubmissionDto>? Answers);

public sealed record SprintReviewItemDto(Guid QuestionId, string QuestionText, bool IsCorrect, string CorrectAnswer);

/// <summary>
/// Result of <c>POST /api/v1/content/quizzes/sprint/finish</c>. <c>Recorded</c> is true only when the
/// caller was signed in (Bearer) and the run reached today's leaderboard; <c>Rank</c> is then set. For an
/// anonymous run, <c>ClaimToken</c> lets the player sign in within an hour and add it via the claim endpoint.
/// </summary>
public sealed record SprintResultDto(
    int Attempted,
    int Correct,
    int Points,
    int BestStreak,
    bool Recorded,
    int? Rank,
    IReadOnlyList<SprintReviewItemDto> Answers,
    string? ClaimToken = null);

/// <summary>Request body for <c>POST /api/v1/content/quizzes/sprint/claim</c>.</summary>
public sealed record SprintClaimRequestDto(string? ClaimToken);

/// <summary><c>Status</c> is <c>claimed</c> or <c>already_claimed</c>; <c>Rank</c> is today's rank when the run was today's.</summary>
public sealed record SprintClaimResultDto(string Status, int Points, int? Rank);

/// <param name="Runs">Runs the member has played in the scope (1 for a single best run; the run count for the total board).</param>
public sealed record SprintLeaderboardEntryDto(int Rank, string DisplayName, int Score, int BestStreak, int Runs = 1);

/// <summary>
/// Shape for <c>GET /api/v1/content/quizzes/sprint/leaderboard</c>: <c>Scope</c> is <c>daily</c>,
/// <c>all</c> (best run ever) or <c>total</c> (points summed over every run); <c>Players</c> counts
/// the members ranked in that scope.
/// </summary>
public sealed record SprintBoardDto(
    string Scope,
    IReadOnlyList<SprintLeaderboardEntryDto> Top,
    SprintLeaderboardEntryDto? Viewer,
    int Players);

/// <summary>Shape for <c>GET /api/v1/content/quizzes/sprint/daily</c> (today, UTC).</summary>
public sealed record SprintDailyBoardDto(
    IReadOnlyList<SprintLeaderboardEntryDto> Top,
    SprintLeaderboardEntryDto? Viewer,
    int PlayersToday);

/// <summary>
/// Category card for <c>/api/v1/content/photos/categories</c> and
/// <c>/api/v1/content/photos/categories/{slug}</c>. Cover URLs are CDN
/// (<c>cdn.queenzone.org</c>) via <see cref="QueenZone.Data.PhotoImageUrl"/>.
/// </summary>
public sealed record PhotoCategoryListItemDto(
    int CatId,
    string Name,
    string Slug,
    int ImageCount,
    string? CoverThumbnailUrl,
    string DetailPath);

/// <summary>
/// Thumbnail-grid card for <c>/api/v1/content/photos/categories/{slug}/items</c>.
/// Includes the CDN thumbnail only — full <c>ImageUrl</c> is reserved for detail
/// so clients do not load originals in a gallery grid.
/// </summary>
public sealed record PhotoListItemDto(
    int PicId,
    int CatId,
    string CategoryName,
    string CategorySlug,
    string Title,
    string ThumbnailUrl,
    int ThumbWidth,
    int ThumbHeight,
    int PictureWidth,
    int PictureHeight,
    string? PictureDimensionsLabel,
    int Year,
    DateTime DateTime,
    string DetailPath,
    string CategoryPath);

/// <summary>
/// Detail shape for <c>/api/v1/content/photos/categories/{slug}/items/{picId}</c>,
/// including prev/next neighbors (same order as the website lightbox).
/// <c>ImageUrl</c> is the CDN original from <see cref="QueenZone.Data.PhotoImageUrl"/>.
/// </summary>
public sealed record PhotoDetailDto(
    int PicId,
    int CatId,
    string CategoryName,
    string CategorySlug,
    string Title,
    string ImageUrl,
    string ThumbnailUrl,
    int ThumbWidth,
    int ThumbHeight,
    int PictureWidth,
    int PictureHeight,
    string? PictureDimensionsLabel,
    int Year,
    DateTime DateTime,
    string? SubmittedByDisplayName,
    string DetailPath,
    string CategoryPath,
    int Index,
    int Count,
    PhotoNavDto? Previous,
    PhotoNavDto? Next);

/// <summary>
/// Minimal reference to an adjacent photo for prev/next navigation.
/// </summary>
public sealed record PhotoNavDto(int PicId, string DetailPath);

/// <summary>
/// List and detail shape for <c>/api/v1/content/fan-performances</c>.
/// <c>AudioPath</c> is the member-gated stream; the listing itself is public,
/// matching <c>/fan-performances</c>. Duration is MPEG metadata when the
/// songfile is readable, otherwise the optional domain value (sample data).
/// </summary>
public sealed record FanPerformanceDto(
    int Id,
    string Title,
    string PerformedBy,
    string Description,
    DateTime DateAdded,
    int? DurationSeconds,
    string DetailPath,
    string AudioPath,
    Guid? ContributorMemberId = null,
    string? ContributorDisplayName = null);
