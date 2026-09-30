using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.NewsAgent;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

internal sealed class ConfigurableNewsAiClient : INewsAiClient
{
    internal const string SampleDraftJson = """
        {
          "title": "Queen announce 2026 tour",
          "slug": "queen-announce-2026-tour",
          "excerpt": "Queen have announced new 2026 tour dates.",
          "body": "Queen will return to the road in 2026 with dates across Europe and the UK.",
          "related_entities": ["Queen", "tour"],
          "source_urls": ["https://www.queenonline.com/news/tour-2026"],
          "source_names": ["Queen Online"],
          "attribution_text": "Source: Queen Online",
          "confidence_notes": "Primary official source.",
          "source_notes": "Official Queen Online announcement.",
          "suggested_publish_at": "2026-07-02T10:00:00Z",
          "secondary_source_warning": false
        }
        """;

    public bool IsEnabled { get; set; } = true;

    public string Content { get; set; } = SampleDraftJson;

    public void Reset()
    {
        IsEnabled = true;
        Content = SampleDraftJson;
    }

    public Task<NewsAiChatCompletion> CompleteChatAsync(
        NewsAiChatRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NewsAiChatCompletion(
            Content,
            "openai/gpt-4.1-mini",
            1,
            1,
            0.0001m,
            false));
}

internal sealed class StubGoogleAnalyticsTrafficService(GoogleAnalyticsTrafficSnapshot snapshot)
    : IGoogleAnalyticsTrafficService
{
    public Task<GoogleAnalyticsTrafficSnapshot> GetDashboardTrafficAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(snapshot);
}

internal sealed class ThrowingPublishHomePollRepository(
    IHomePollRepository inner,
    Exception exception) : IHomePollRepository
{
    public Task<HomePollResults?> GetCurrentAsync(
        Guid? viewerMemberId,
        CancellationToken cancellationToken = default) =>
        inner.GetCurrentAsync(viewerMemberId, cancellationToken);

    public Task<IReadOnlyList<HomePollAdminItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<HomePollAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.GetByIdAsync(id, cancellationToken);

    public Task<Guid> CreateAsync(
        AdminHomePollDraft draft,
        Guid createdByMemberId,
        CancellationToken cancellationToken = default) =>
        inner.CreateAsync(draft, createdByMemberId, cancellationToken);

    public Task UpdateAsync(Guid id, AdminHomePollDraft draft, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(id, draft, cancellationToken);

    public Task PublishAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw exception;

    public Task CloseAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.CloseAsync(id, cancellationToken);

    public Task HideAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.HideAsync(id, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(id, cancellationToken);

    public Task CastVoteAsync(Guid optionId, Guid memberId, CancellationToken cancellationToken = default) =>
        inner.CastVoteAsync(optionId, memberId, cancellationToken);
}

internal sealed class TimeoutHideForumWriteRepository(
    IForumWriteRepository inner,
    Exception timeout) : IForumWriteRepository
{
    public Task<ForumThreadCreateResult> CreateThreadAsync(
        NewForumThread thread, CancellationToken cancellationToken = default) =>
        inner.CreateThreadAsync(thread, cancellationToken);

    public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
        inner.CreatePostAsync(post, cancellationToken);

    public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
        inner.GetPostAsync(postId, cancellationToken);

    public Task<ForumPostUpdateResult> UpdatePostAsync(
        int postId,
        Guid editorMemberId,
        string sanitisedBody,
        bool isAdmin,
        int editWindowMinutes,
        DateTimeOffset? expectedUpdatedAt = null,
        CancellationToken cancellationToken = default) =>
        inner.UpdatePostAsync(
            postId, editorMemberId, sanitisedBody, isAdmin, editWindowMinutes, expectedUpdatedAt, cancellationToken);

    public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
        inner.GetThreadAsync(topicId, cancellationToken);

    public Task<int> CountPostsByMemberSinceAsync(
        Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        inner.CountPostsByMemberSinceAsync(memberId, since, cancellationToken);

    public Task<int> CountApprovedPostsByMemberAsync(
        Guid memberId, CancellationToken cancellationToken = default) =>
        inner.CountApprovedPostsByMemberAsync(memberId, cancellationToken);

    public Task<ForumAuthorContentSummary> GetAuthorForumContentSummaryAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        inner.GetAuthorForumContentSummaryAsync(memberId, displayName, cancellationToken);

    public Task<ForumAuthorContentSummary?> FindNoAccountForumAuthorAsync(
        string displayName, CancellationToken cancellationToken = default) =>
        inner.FindNoAccountForumAuthorAsync(displayName, cancellationToken);

    public Task HideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        throw timeout;

    public Task UnhideAuthorForumContentAsync(
        Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
        inner.UnhideAuthorForumContentAsync(memberId, displayName, cancellationToken);

    public Task<int> EnsureCategoryAsync(
        string slug, string name, CancellationToken cancellationToken = default) =>
        inner.EnsureCategoryAsync(slug, name, cancellationToken);
}

internal sealed class ThrowingRevokeMobileAuthGrantRepository : IMobileAuthGrantRepository
{
    public Task StoreAuthorizationCodeAsync(
        QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity code,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
        string codeHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?>(null);

    public Task StoreRefreshTokenAsync(
        QueenZone.Data.Entities.MobileAuthRefreshTokenEntity token,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?>(null);

    public Task<bool> TryRevokeRefreshTokenAsync(
        string tokenHash,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<int> RevokeAllRefreshTokensForMemberAsync(
        Guid memberAccountId,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("token store unavailable");

    public Task<bool> TryRotateRefreshTokenAsync(
        string oldTokenHash,
        QueenZone.Data.Entities.MobileAuthRefreshTokenEntity replacement,
        DateTime utcNow,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal static class AdminHomePollPublishFailures
{
    internal static DbUpdateException UniqueCurrentConstraint() =>
        new(
            "Cannot insert duplicate key row in object 'dbo.HomePolls' with unique index 'UX_HomePolls_IsCurrent'. The duplicate key value is (1).",
            SqlExceptionFactory.Create(
                2601,
                "Cannot insert duplicate key row in object 'dbo.HomePolls' with unique index 'UX_HomePolls_IsCurrent'. The duplicate key value is (1)."));
}
