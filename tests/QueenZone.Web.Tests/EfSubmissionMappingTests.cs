using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

/// <summary>
/// Each EF submission repository maps a row to its model twice: <c>GetByIdAsync</c> maps a loaded
/// entity, while the member list projects in the query. Every column is filled with a distinct
/// value so a dropped or swapped argument in either mapping shows up as a mismatch.
/// </summary>
public sealed class EfSubmissionMappingTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly Guid memberId = Guid.NewGuid();

    public EfSubmissionMappingTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();

        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = memberId,
            Email = "mapping@example.com",
            NormalizedEmail = "MAPPING@EXAMPLE.COM",
            DisplayName = "Mapping Fan",
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.SaveChanges();
    }

    [Fact]
    public async Task Photo_submission_maps_the_same_by_id_and_in_member_list()
    {
        var entity = Filled(new PhotoSubmissionEntity { SubmitterMemberId = memberId, Status = PhotoSubmissionStatus.Approved });
        dbContext.PhotoSubmissions.Add(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfPhotoSubmissionRepository(dbContext);
        var byId = await repository.GetByIdAsync(entity.Id);
        var listed = Assert.Single((await repository.GetBySubmitterAsync(memberId)).Items);

        Assert.Equal(listed, byId);
        Assert.Equal("Mapping Fan", byId!.SubmitterDisplayName);
        Assert.Equal("mapping@example.com", byId.SubmitterEmail);
    }

    [Fact]
    public async Task News_suggestion_maps_the_same_by_id_and_in_member_list()
    {
        var entity = Filled(new NewsSuggestionEntity { SubmitterMemberId = memberId, Status = NewsSuggestionStatus.Promoted });
        entity.DuplicateCandidateId = null; // foreign key to NewsCandidates
        dbContext.NewsSuggestions.Add(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfNewsSuggestionRepository(dbContext);
        var byId = await repository.GetByIdAsync(entity.Id);
        var listed = Assert.Single((await repository.GetBySubmitterAsync(memberId)).Items);

        Assert.Equal(listed, byId);
        Assert.Equal("Mapping Fan", byId!.SubmitterDisplayName);
    }

    [Fact]
    public async Task Fan_performance_submission_maps_the_same_by_id_and_in_member_list()
    {
        var entity = Filled(new FanPerformanceSubmissionEntity
        {
            SubmitterMemberId = memberId,
            Status = FanPerformanceSubmissionStatus.Approved,
        });
        dbContext.FanPerformanceSubmissions.Add(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfFanPerformanceSubmissionRepository(dbContext);
        var byId = await repository.GetByIdAsync(entity.Id);
        var listed = Assert.Single((await repository.GetBySubmitterAsync(memberId)).Items);

        Assert.Equal(listed, byId);
        Assert.Equal("Mapping Fan", byId!.SubmitterDisplayName);
    }

    [Fact]
    public async Task Trivia_fact_submission_maps_the_same_by_id_and_in_member_list()
    {
        var entity = Filled(new TriviaFactSubmissionEntity { SubmitterMemberId = memberId, Status = TriviaFactSubmissionStatus.Approved });
        dbContext.TriviaFactSubmissions.Add(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfTriviaFactSubmissionRepository(dbContext);
        var byId = await repository.GetByIdAsync(entity.Id);
        var listed = Assert.Single((await repository.GetBySubmitterAsync(memberId)).Items);

        Assert.Equal(listed, byId);
        Assert.Equal("Mapping Fan", byId!.SubmitterDisplayName);
    }

    [Fact]
    public async Task Article_submission_maps_the_same_by_id_and_in_member_drafts()
    {
        var entity = Filled(new ArticleSubmissionEntity { AuthorMemberId = memberId, Status = ArticleSubmissionStatus.Published });
        dbContext.ArticleSubmissions.Add(entity);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var repository = new EfArticleSubmissionRepository(dbContext);
        var byId = await repository.GetByIdAsync(entity.Id);
        var listed = Assert.Single((await repository.GetDraftsForMemberAsync(memberId)).Items);

        Assert.Equal(listed, byId);
        Assert.Equal("Mapping Fan", byId!.AuthorDisplayName);
    }

    [Fact]
    public async Task Created_submission_is_normalized_and_mapped()
    {
        var repository = new EfNewsSuggestionRepository(dbContext);
        var created = await repository.CreateAsync(new NewsSuggestion(
            Guid.Empty,
            memberId,
            " https://example.com/story ",
            "hash-created",
            "  Title  ",
            "   ",
            NewsSuggestionStatus.Pending,
            default,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("https://example.com/story", created.Url);
        Assert.Equal("Title", created.Title);
        Assert.Null(created.Notes);

        // The member is already tracked, so EF fixes up the Submitter navigation on the new row.
        Assert.Equal("Mapping Fan", created.SubmitterDisplayName);
        Assert.Equal("mapping@example.com", created.SubmitterEmail);
    }

    // Sets every scalar column (other than keys, the member id and status) to a distinct value.
    private static T Filled<T>(T entity)
        where T : class
    {
        var index = 0;
        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite
                || property.Name is "Status" or "SubmitterMemberId" or "AuthorMemberId")
            {
                continue;
            }

            index++;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? value = type switch
            {
                _ when type == typeof(Guid) => Guid.NewGuid(),
                _ when type == typeof(string) => $"{property.Name}-{index}",
                _ when type == typeof(int) => 1000 + index,
                _ when type == typeof(long) => 100_000L + index,
                _ when type == typeof(DateTimeOffset) =>
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(index),
                _ when type == typeof(DateOnly) => new DateOnly(1986, 7, 12).AddDays(index),
                _ => null,
            };

            if (value is not null)
            {
                property.SetValue(entity, value);
            }
        }

        return entity;
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
