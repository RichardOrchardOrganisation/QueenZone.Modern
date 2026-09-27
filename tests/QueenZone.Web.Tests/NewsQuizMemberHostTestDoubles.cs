using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal sealed class WarmupThrowingNewsRepository : INewsRepository
{
    public Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("simulated repository failure");

    public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsItem>>([]);

    public Task<int> GetPublishedCountAsync(NewsArchiveFilter filter = default, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new NewsArchiveYearRange(null, null));

    public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<NewsItem?>(null);

    public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsItem>>([]);

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SitemapContentEntry>>([]);

    public Task<NewsSearchPage> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new NewsSearchPage([], 0, page, pageSize));
}

internal sealed class ThrowingSprintBoardQuizRepository : IQuizRepository
{
    public Task<QuizSprintBoardResult> GetSprintBoardAsync(
        QuizSprintBoardScope scope,
        Guid? viewerMemberId,
        int top = 10,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated QuizSprintRuns lookup failure.");

    public Task<IReadOnlyList<QuizAdminItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Unsupported<IReadOnlyList<QuizAdminItem>>();

    public Task<QuizAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Unsupported<QuizAdminDetail?>();

    public Task<Guid> CreateAsync(
        AdminQuizDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default) =>
        Unsupported<Guid>();

    public Task UpdateAsync(Guid id, AdminQuizDraft draft, CancellationToken cancellationToken = default) =>
        Unsupported();

    public Task PublishAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

    public Task UnpublishAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Unsupported();

    public Task RecordAttemptAsync(
        Guid quizId,
        Guid memberAccountId,
        int score,
        int correctCount,
        int questionCount,
        CancellationToken cancellationToken = default) =>
        Unsupported();

    public Task<IReadOnlyList<QuizListItem>> GetPublishedAsync(CancellationToken cancellationToken = default) =>
        Unsupported<IReadOnlyList<QuizListItem>>();

    public Task<QuizPlayView?> GetPublishedForPlayAsync(Guid id, CancellationToken cancellationToken = default) =>
        Unsupported<QuizPlayView?>();

    public Task<IReadOnlyList<QuizSprintQuestion>> GetPublishedSprintQuestionsAsync(
        CancellationToken cancellationToken = default) =>
        Unsupported<IReadOnlyList<QuizSprintQuestion>>();

    public Task<QuizSubmissionResult?> SubmitAsync(
        Guid quizId,
        Guid? memberAccountId,
        IReadOnlyList<QuizAnswerSubmission> answers,
        CancellationToken cancellationToken = default) =>
        Unsupported<QuizSubmissionResult?>();

    public Task<QuizLeaderboardResult> GetLeaderboardAsync(
        QuizLeaderboardScope scope,
        Guid? viewerMemberId,
        int top = 10,
        CancellationToken cancellationToken = default) =>
        Unsupported<QuizLeaderboardResult>();

    public Task RecordSprintRunAsync(
        Guid memberAccountId,
        QuizSprintScore score,
        CancellationToken cancellationToken = default) =>
        Unsupported();

    public Task<bool> ClaimSprintRunAsync(
        Guid runId,
        Guid memberAccountId,
        QuizSprintScore score,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default) =>
        Unsupported<bool>();

    private static Task Unsupported() => Task.FromException(new NotSupportedException());

    private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
}

internal sealed class SeedableMemberPageActivityRepository : IMemberPublicActivityRepository
{
    private IReadOnlyList<MemberPublicActivityItem> items = [];

    public void Seed(IReadOnlyList<MemberPublicActivityItem> activity) => items = activity;

    public void Reset() => items = [];

    public Task<MemberPublicActivityPage> GetPageAsync(
        Guid memberId,
        int? linkedLegacyUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var pageItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new MemberPublicActivityPage(pageItems, items.Count, page, pageSize));
    }

    public Task<MemberPublicActivityPage> GetFeedPageAsync(
        IReadOnlyCollection<Guid> memberIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Following feed should not N+1 through GetPageAsync.");
}
