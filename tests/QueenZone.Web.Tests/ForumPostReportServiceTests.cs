using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumPostReportServiceTests
{
    [Fact]
    public async Task ReportAsync_ValidatesCategoryAndDetails()
    {
        var service = CreateService(out _);
        var reporter = Guid.NewGuid();

        var category = await service.ReportAsync(reporter, 1101, "Unknown", null);
        var details = await service.ReportAsync(reporter, 1101, ForumPostReportCategories.Other,
            new string('x', ForumPostReportLimits.MaxDetailsLength + 1));

        Assert.Equal(ForumPostReportText.CategoryRequired, category.ErrorMessage);
        Assert.Equal(ForumPostReportText.DetailsTooLong, details.ErrorMessage);
    }

    [Fact]
    public async Task ReportAsync_SnapshotsLegacyPost_AndIsIdempotent()
    {
        var service = CreateService(out var repository);
        var reporter = Guid.NewGuid();

        var first = await service.ReportAsync(reporter, 1101, ForumPostReportCategories.Harassment, "  context  ");
        var second = await service.ReportAsync(reporter, 1101, ForumPostReportCategories.Spam, null);
        var report = await repository.GetAsync(first.ReportId!.Value);

        Assert.True(first.Succeeded);
        Assert.False(first.AlreadyReported);
        Assert.True(second.AlreadyReported);
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal("context", report!.Details);
        Assert.Equal("jazzfanz", report.AuthorDisplayNameSnapshot);
        Assert.Contains("Top tier", report.PostBodySnapshot);
        Assert.Null(report.ReportedMemberId);
    }

    [Fact]
    public async Task ReportAsync_RejectsOwnOrMissingPost()
    {
        var writes = new InMemoryForumWriteRepository();
        var author = Guid.NewGuid();
        var postId = await writes.CreatePostAsync(new NewForumPost(1002, author, "Author", "Body", DateTimeOffset.UtcNow));
        var repository = new InMemoryForumPostReportRepository(writes);
        var service = new ForumPostReportService(repository, TimeProvider.System);

        var own = await service.ReportAsync(author, postId, ForumPostReportCategories.Other, null);
        var missing = await service.ReportAsync(Guid.NewGuid(), int.MaxValue, ForumPostReportCategories.Other, null);

        Assert.Equal(ForumPostReportText.CannotReportOwn, own.ErrorMessage);
        Assert.Equal(ForumPostReportText.PostNotFound, missing.ErrorMessage);
    }

    [Fact]
    public async Task Repository_ListsTransitionsAndCountsReports()
    {
        var service = CreateService(out var repository);
        var created = await service.ReportAsync(Guid.NewGuid(), 1102, ForumPostReportCategories.Spam, null);

        Assert.Equal(1, await repository.CountOpenAsync());
        Assert.Contains(1102, await repository.GetReportedPostIdsAsync(
            (await repository.GetAsync(created.ReportId!.Value))!.ReporterMemberId, [1101, 1102]));
        var page = await repository.ListAsync(PrivateMessageReportStatus.Open, 1, 20);
        Assert.Single(page.Items);

        var updated = await repository.UpdateStatusAsync(created.ReportId.Value, PrivateMessageReportStatus.Actioned, "admin@test.local");
        Assert.Equal(PrivateMessageReportStatus.Actioned, updated!.Status);
        Assert.Equal(0, await repository.CountOpenAsync());
        Assert.Null(await repository.UpdateStatusAsync(Guid.NewGuid(), PrivateMessageReportStatus.Reviewed, "admin@test.local"));
        await repository.AppendViewedAuditAsync(created.ReportId.Value, "admin@test.local");
    }

    [Fact]
    public void Categories_AreStrict()
    {
        Assert.All(ForumPostReportCategories.All, category => Assert.True(ForumPostReportCategories.IsKnown(category)));
        Assert.False(ForumPostReportCategories.IsKnown(null));
        Assert.False(ForumPostReportCategories.IsKnown(" harassment or bullying "));
    }

    private static ForumPostReportService CreateService(out InMemoryForumPostReportRepository repository)
    {
        repository = new InMemoryForumPostReportRepository(new InMemoryForumWriteRepository());
        return new ForumPostReportService(repository, TimeProvider.System);
    }
}
