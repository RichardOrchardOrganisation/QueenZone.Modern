using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Maps repository DTOs directly to <c>/api/v1/content</c> JSON shapes.
/// Kept separate from <see cref="PublicContentMapper"/>, which serves Razor Pages.
/// </summary>
public static class ContentApiMapper
{
    public static NewsListItemDto ToNewsListItem(
        NewsItem item,
        IReadOnlyDictionary<int, int>? replyCounts = null) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.PublishedAt,
            NewsRoutes.GetNewsDetailPath(item.Id, item.Title, item.Slug),
            NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            NewsArticleImage.ResolveThumbnailUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            item.ForumTopicId,
            ReplyCountFor(item.ForumTopicId, replyCounts));

    public static IReadOnlyList<NewsListItemDto> ToNewsListItems(IEnumerable<NewsItem> items) =>
        items.Select(item => ToNewsListItem(item)).ToList();

    public static NewsDetailDto ToNewsDetail(
        NewsItem item,
        int? discussionReplyCount = null,
        IReadOnlyList<NewsDiscussionPreviewDto>? discussionPreview = null) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            // Same sanitization / plain-text autolink path as the website news detail page.
            NewsArticleContent.FormatBody(item.Body),
            item.PublishedAt,
            item.SourceUrl,
            NewsRoutes.GetNewsDetailPath(item.Id, item.Title, item.Slug),
            NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            NewsArticleImage.ResolveThumbnailUrl(item.ImageBlobKey, item.ImageGalleryPicId),
            item.ForumTopicId,
            item.ForumTopicId is null ? null : discussionReplyCount,
            item.ForumTopicId is null ? null : discussionPreview);

    private static int? ReplyCountFor(int? topicId, IReadOnlyDictionary<int, int>? replyCounts)
    {
        if (topicId is not int id)
        {
            return null;
        }

        return replyCounts is not null && replyCounts.TryGetValue(id, out var count) ? count : 0;
    }

    public static ArticleListItemDto ToArticleListItem(ArticleItem item) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            item.PublishedAt,
            ArticlesRoutes.GetArticleDetailPath(item),
            item.CategoryName);

    public static IReadOnlyList<ArticleListItemDto> ToArticleListItems(IEnumerable<ArticleItem> items) =>
        items.Select(ToArticleListItem).ToList();

    public static ArticleDetailDto ToArticleDetail(ArticleItem item) =>
        new(
            item.Id,
            item.Title,
            item.Excerpt,
            ArticleContent.FormatBody(item.Body),
            item.PublishedAt,
            item.Source,
            item.CategoryName,
            ArticlesRoutes.GetArticleDetailPath(item));

    public static QuoteDto ToQuoteDto(QuoteItem quote) =>
        new(quote.Id, quote.Text, quote.WhoSaid, string.IsNullOrWhiteSpace(quote.Context) ? null : quote.Context);

    public static TriviaDto ToTriviaDto(TriviaFactItem fact) =>
        new(
            fact.Id,
            fact.Text,
            string.IsNullOrWhiteSpace(fact.Category) ? null : fact.Category,
            string.IsNullOrWhiteSpace(fact.Difficulty) ? null : fact.Difficulty,
            string.IsNullOrWhiteSpace(fact.Source) ? null : fact.Source);

    public static HomePollDto ToHomePollDto(HomePollResults poll) =>
        new(
            poll.PollId,
            poll.Question,
            poll.Options
                .Select(option => new HomePollOptionDto(
                    option.OptionId,
                    option.OptionText,
                    option.VoteCount,
                    option.Percentage))
                .ToList(),
            poll.TotalVotes,
            poll.IsClosed,
            poll.ViewerHasVoted,
            poll.SelectedOptionId);

    public static QuizListItemDto ToQuizListItemDto(QuizListItem quiz) =>
        new(quiz.Id, quiz.Title, quiz.Description, quiz.QuestionCount);

    public static QuizDetailDto ToQuizDetailDto(QuizPlayView quiz) =>
        new(
            quiz.Id,
            quiz.Title,
            quiz.Description,
            quiz.Questions
                .Select(question => new QuizQuestionDto(
                    question.Id,
                    question.Text,
                    question.Points,
                    question.Options
                        .Select(option => new QuizOptionDto(option.Id, option.Text))
                        .ToList()))
                .ToList());

    public static QuizResultDto ToQuizResultDto(QuizSubmissionResult result) =>
        new(
            result.QuizId,
            result.QuizTitle,
            result.Score,
            result.MaxScore,
            result.CorrectCount,
            result.QuestionCount,
            result.Recorded,
            result.Answers
                .Select(answer => new QuizAnswerResultDto(
                    answer.QuestionId,
                    answer.QuestionText,
                    answer.SelectedOptionId,
                    answer.SelectedOptionText,
                    answer.CorrectOptionId,
                    answer.CorrectOptionText,
                    answer.IsCorrect,
                    answer.PointsAwarded))
                .ToList());

    public static TimelineEventDto ToTimelineEvent(QueenHistoryEvent historyEvent) =>
        new(
            historyEvent.Id,
            historyEvent.Title,
            historyEvent.Summary,
            historyEvent.EventDate,
            historyEvent.FormattedDate,
            ToTimelineCategory(historyEvent.Category),
            ToTimelineCategoryLabel(historyEvent.Category),
            historyEvent.SourceUrl);

    public static IReadOnlyList<TimelineEventDto> ToTimelineEvents(IEnumerable<QueenHistoryEvent> events) =>
        events.Select(ToTimelineEvent).ToList();

    private static string ToTimelineCategory(QueenHistoryEventCategory category) => category switch
    {
        QueenHistoryEventCategory.Concert => "live",
        QueenHistoryEventCategory.Release or QueenHistoryEventCategory.Recording => "music",
        QueenHistoryEventCategory.Award or QueenHistoryEventCategory.Birthday or QueenHistoryEventCategory.SiteHistory => "milestone",
        _ => "other",
    };

    private static string ToTimelineCategoryLabel(QueenHistoryEventCategory category) => category switch
    {
        QueenHistoryEventCategory.Concert => "Live",
        QueenHistoryEventCategory.Release => "Release",
        QueenHistoryEventCategory.Recording => "Recording",
        QueenHistoryEventCategory.Award => "Award",
        QueenHistoryEventCategory.Birthday => "Birthday",
        QueenHistoryEventCategory.TVRadio => "TV / Radio",
        QueenHistoryEventCategory.SiteHistory => "Archive",
        _ => "Other",
    };

    public static BiographyChapterListItemDto ToBiographyChapterListItem(BiographyChapterItem chapter) =>
        new(
            chapter.Id,
            chapter.Title,
            BiographyContent.GetListSummary(chapter),
            chapter.DisplaySequence,
            BiographyRoutes.GetChapterDetailPath(chapter));

    public static IReadOnlyList<BiographyChapterListItemDto> ToBiographyChapterListItems(
        IEnumerable<BiographyChapterItem> chapters) =>
        chapters.Select(ToBiographyChapterListItem).ToList();

    public static BiographyChapterDetailDto ToBiographyChapterDetail(
        BiographyChapterItem chapter,
        BiographyChapterNav navigation) =>
        new(
            chapter.Id,
            chapter.Title,
            BiographyContent.GetListSummary(chapter),
            BiographyContent.FormatBody(chapter.Body),
            chapter.DisplaySequence,
            BiographyRoutes.GetChapterDetailPath(chapter),
            ToBiographyChapterNavDto(navigation.Previous),
            ToBiographyChapterNavDto(navigation.Next));

    private static BiographyChapterNavDto? ToBiographyChapterNavDto(BiographyChapterItem? chapter) =>
        chapter is null
            ? null
            : new BiographyChapterNavDto(chapter.Id, chapter.Title, BiographyRoutes.GetChapterDetailPath(chapter));

    public static AlbumListItemDto ToAlbumListItem(AlbumSummary album) =>
        new(
            album.AlbumId,
            album.Name,
            album.ReleaseYear,
            album.ThumbnailUrl,
            DiscographyRoutes.GetAlbumPath(album));

    public static IReadOnlyList<AlbumListItemDto> ToAlbumListItems(IEnumerable<AlbumSummary> albums) =>
        albums.Select(ToAlbumListItem).ToList();

    public static AlbumDetailDto ToAlbumDetail(AlbumDetail album) =>
        new(
            album.AlbumId,
            album.Name,
            album.ReleaseYear,
            album.ArtistName,
            album.GeneralNotes is null ? null : NewsArticleContent.FormatBody(album.GeneralNotes),
            album.CoverUrl,
            DiscographyRoutes.GetAlbumPath(album.AlbumId, album.Slug),
            ToAlbumSongs(album.Songs));

    private static IReadOnlyList<AlbumSongDto> ToAlbumSongs(IEnumerable<AlbumSong> songs) =>
        songs
            .Select(song => new AlbumSongDto(
                song.SongId,
                song.Title,
                song.IsSingle,
                song.Lyrics is null ? null : LyricsFormatter.Format(song.Lyrics),
                song.Notes))
            .ToList();

    public static FreddieTributeDto ToFreddieTributeDto(FreddieTribute tribute) =>
        new(
            tribute.Id,
            tribute.Name,
            tribute.Thought,
            tribute.Country,
            tribute.DateText,
            tribute.TimeText);

    public static IReadOnlyList<FreddieTributeDto> ToFreddieTributeDtos(IEnumerable<FreddieTribute> tributes) =>
        tributes.Select(ToFreddieTributeDto).ToList();

    public static PhotoCategoryListItemDto ToPhotoCategoryListItem(PhotoCategory category) =>
        new(
            category.CatId,
            category.Name,
            category.Slug,
            category.ImageCount,
            category.CoverThumbnailUrl,
            PhotoRoutes.GetCategoryPath(category.Slug));

    public static IReadOnlyList<PhotoCategoryListItemDto> ToPhotoCategoryListItems(
        IEnumerable<PhotoCategory> categories) =>
        categories.Select(ToPhotoCategoryListItem).ToList();

    public static PhotoListItemDto ToPhotoListItem(PhotoItem item, PhotoListFilter? filter = null) =>
        new(
            item.PicId,
            item.CatId,
            item.CategoryName,
            item.CategorySlug,
            item.Title,
            item.ThumbnailUrl,
            item.ThumbWidth,
            item.ThumbHeight,
            item.PictureWidth,
            item.PictureHeight,
            item.PictureDimensionsLabel,
            item.Year,
            item.DateTime,
            PhotoRoutes.GetDetailPath(item.CategorySlug, item.PicId, filter),
            PhotoRoutes.GetCategoryPath(item.CategorySlug, filter));

    public static IReadOnlyList<PhotoListItemDto> ToPhotoListItems(
        IEnumerable<PhotoItem> items,
        PhotoListFilter? filter = null) =>
        items.Select(item => ToPhotoListItem(item, filter)).ToList();

    public static PhotoDetailDto ToPhotoDetail(
        PhotoCategory category,
        PhotoDetailNavigation navigation,
        PhotoListFilter? filter = null)
    {
        var photo = navigation.Photo;
        return new PhotoDetailDto(
            photo.PicId,
            photo.CatId,
            category.Name,
            category.Slug,
            photo.Title,
            photo.ImageUrl,
            photo.ThumbnailUrl,
            photo.ThumbWidth,
            photo.ThumbHeight,
            photo.PictureWidth,
            photo.PictureHeight,
            photo.PictureDimensionsLabel,
            photo.Year,
            photo.DateTime,
            photo.SubmittedByDisplayName,
            PhotoRoutes.GetDetailPath(category.Slug, photo.PicId, filter),
            PhotoRoutes.GetCategoryPath(category.Slug, filter),
            navigation.Index,
            navigation.Count,
            ToPhotoNavDto(category.Slug, navigation.PreviousPicId, filter),
            ToPhotoNavDto(category.Slug, navigation.NextPicId, filter));
    }

    private static PhotoNavDto? ToPhotoNavDto(string slug, int? picId, PhotoListFilter? filter) =>
        picId is int id
            ? new PhotoNavDto(id, PhotoRoutes.GetDetailPath(slug, id, filter))
            : null;

    public static FanPerformanceDto ToFanPerformanceDto(FanPerformance performance, int? durationSeconds) =>
        new(
            performance.Id,
            performance.Title,
            performance.PerformedBy,
            performance.Description,
            performance.DateAdded,
            durationSeconds,
            FanPerformanceRoutes.GetIndexPath(),
            FanPerformanceRoutes.GetApiAudioPath(performance.Id),
            performance.ContributorMemberId,
            performance.ContributorDisplayName);

    public static IReadOnlyList<FanPerformanceDto> ToFanPerformanceDtos(
        IReadOnlyList<FanPerformance> items,
        IReadOnlyList<int?> durations)
    {
        var mapped = new FanPerformanceDto[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var duration = i < durations.Count ? durations[i] : items[i].DurationSeconds;
            mapped[i] = ToFanPerformanceDto(items[i], duration);
        }

        return mapped;
    }
}
