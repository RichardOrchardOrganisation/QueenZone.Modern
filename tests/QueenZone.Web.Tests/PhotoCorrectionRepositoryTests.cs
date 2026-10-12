using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

/// <summary>
/// The same contract run against the EF (SQLite) and in-memory repositories so the two
/// cannot drift. SQL Server locking is not established by SQLite; see the opt-in probes.
/// </summary>
public sealed class PhotoCorrectionRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly Guid memberId = Guid.NewGuid();
    private readonly Guid otherMemberId = Guid.NewGuid();

    public PhotoCorrectionRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
        foreach (var (id, name) in new[] { (memberId, "Fan One"), (otherMemberId, "Fan Two") })
        {
            dbContext.MemberAccounts.Add(new MemberAccount
            {
                Id = id,
                Email = $"{name.Replace(' ', '-')}@example.com",
                NormalizedEmail = $"{name.Replace(' ', '-')}@EXAMPLE.COM".ToUpperInvariant(),
                DisplayName = name,
                CreatedAt = DateTime.UtcNow,
            });
        }

        dbContext.SaveChanges();
    }

    public static TheoryData<string> Implementations => new() { "ef", "memory" };

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Create_stores_pending_proposal_with_audit_and_hides_nothing_from_owner(string implementation)
    {
        var repository = Create(implementation);

        var created = await repository.CreateAsync(New(picId: 101));

        Assert.Equal(PhotoCorrectionStatus.Pending, created.Status);
        var loaded = await repository.GetByIdAsync(created.Id);
        Assert.Equal("Soundcheck, Wembley", loaded!.ProposedValue);
        Assert.Equal("https://example.com/ticket", loaded.EvidenceUrl);
        var audit = Assert.Single(await repository.GetAuditAsync(created.Id));
        Assert.Equal("Submitted", audit.Action);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Create_with_the_same_id_is_an_idempotent_retry(string implementation)
    {
        var repository = Create(implementation);
        var id = Guid.NewGuid();

        var first = await repository.CreateAsync(New(picId: 101, id: id));
        var retry = await repository.CreateAsync(New(picId: 101, id: id));

        Assert.Equal(first.Id, retry.Id);
        Assert.Single(await repository.GetForPhotoAsync(101));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Create_refuses_a_second_open_proposal_for_the_same_photo_and_field(string implementation)
    {
        var repository = Create(implementation);
        await repository.CreateAsync(New(picId: 101));

        await Assert.ThrowsAsync<DuplicatePhotoCorrectionException>(
            () => repository.CreateAsync(New(picId: 101)));

        // A different field, a different photo, or a different member is fine.
        await repository.CreateAsync(New(picId: 101, kind: PhotoCorrectionKind.EventDate));
        await repository.CreateAsync(New(picId: 102));
        await repository.CreateAsync(New(picId: 101, member: otherMemberId));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Create_allows_a_new_proposal_once_the_earlier_one_is_decided(string implementation)
    {
        var repository = Create(implementation);
        var first = await repository.CreateAsync(New(picId: 101));
        await repository.RejectAsync(first.Id, "ed@example.com", "Not enough evidence", null);

        var second = await repository.CreateAsync(New(picId: 101));

        Assert.Equal(PhotoCorrectionStatus.Pending, second.Status);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Create_caps_open_proposals_per_member(string implementation)
    {
        var repository = Create(implementation);
        for (var picId = 1; picId <= PhotoCorrectionValidation.MaxOpenPerMember; picId++)
        {
            await repository.CreateAsync(New(picId));
        }

        await Assert.ThrowsAsync<PhotoCorrectionLimitException>(
            () => repository.CreateAsync(New(picId: 999)));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Evidence_urls_that_are_not_plain_http_are_never_stored(string implementation)
    {
        var repository = Create(implementation);

        var created = await repository.CreateAsync(New(picId: 101, evidenceUrl: "javascript:alert(1)"));

        Assert.Null(created.EvidenceUrl);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Approve_records_reviewer_applied_value_and_audit(string implementation)
    {
        var repository = Create(implementation);
        var created = await repository.CreateAsync(New(picId: 101));

        var approved = await repository.ApproveAsync(created.Id, "ed@example.com", "Checked ticket", "Old caption");

        Assert.Equal(PhotoCorrectionStatus.Approved, approved!.Status);
        Assert.Equal("Old caption", approved.AppliedFromValue);
        Assert.NotNull(approved.AppliedAt);
        Assert.Equal("ed@example.com", approved.ReviewerEmail);
        Assert.Equal(["Submitted", "Approved"], (await repository.GetAuditAsync(created.Id)).Select(a => a.Action));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Approve_without_applied_value_does_not_claim_the_photo_changed(string implementation)
    {
        var repository = Create(implementation);
        var created = await repository.CreateAsync(New(picId: 101, kind: PhotoCorrectionKind.EventDate));

        var approved = await repository.ApproveAsync(created.Id, "ed@example.com", null, null);

        Assert.Null(approved!.AppliedFromValue);
        Assert.Null(approved.AppliedAt);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Request_info_keeps_the_proposal_open_and_shows_a_message(string implementation)
    {
        var repository = Create(implementation);
        var created = await repository.CreateAsync(New(picId: 101));

        var asked = await repository.RequestInfoAsync(created.Id, "ed@example.com", "Which show was this?", "internal");

        Assert.Equal(PhotoCorrectionStatus.NeedsInfo, asked!.Status);
        Assert.Equal("Which show was this?", asked.MessageToMember);
        Assert.Contains(await repository.GetOpenAsync(1, 10), item => item.Id == created.Id);
        await Assert.ThrowsAsync<DuplicatePhotoCorrectionException>(() => repository.CreateAsync(New(picId: 101)));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Reject_requires_a_message_and_leaves_the_queue(string implementation)
    {
        var repository = Create(implementation);
        var created = await repository.CreateAsync(New(picId: 101));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.RejectAsync(created.Id, "ed@example.com", "  ", null));
        Assert.Equal(PhotoCorrectionStatus.Pending, (await repository.GetByIdAsync(created.Id))!.Status);

        await repository.RejectAsync(created.Id, "ed@example.com", "No source", null);

        Assert.DoesNotContain(await repository.GetOpenAsync(1, 10), item => item.Id == created.Id);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Decided_proposals_cannot_be_decided_again_and_only_approved_can_revert(string implementation)
    {
        var repository = Create(implementation);
        var rejected = await repository.CreateAsync(New(picId: 101));
        await repository.RejectAsync(rejected.Id, "ed@example.com", "No", null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.ApproveAsync(rejected.Id, "ed@example.com", null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.RevertAsync(rejected.Id, "ed@example.com", null));

        var approved = await repository.CreateAsync(New(picId: 102));
        await repository.ApproveAsync(approved.Id, "ed@example.com", null, "Before");
        var reverted = await repository.RevertAsync(approved.Id, "ed@example.com", "Wrong show");

        Assert.Equal(PhotoCorrectionStatus.Reverted, reverted!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.RevertAsync(approved.Id, "ed@example.com", null));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Decisions_on_unknown_ids_return_null(string implementation)
    {
        var repository = Create(implementation);
        var missing = Guid.NewGuid();

        Assert.Null(await repository.GetByIdAsync(missing));
        Assert.Null(await repository.ApproveAsync(missing, "ed@example.com", null, null));
        Assert.Null(await repository.RejectAsync(missing, "ed@example.com", "x", null));
        Assert.Null(await repository.RequestInfoAsync(missing, "ed@example.com", "x", null));
        Assert.Null(await repository.RevertAsync(missing, "ed@example.com", null));
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Open_queue_is_oldest_first_and_competing_proposals_are_listed_per_photo(string implementation)
    {
        var repository = Create(implementation);
        var first = await repository.CreateAsync(New(picId: 101));
        await Task.Delay(5);
        var second = await repository.CreateAsync(New(picId: 101, member: otherMemberId, proposed: "Rival caption"));

        var queue = await repository.GetOpenAsync(1, 10);
        var competing = await repository.GetForPhotoAsync(101);

        Assert.Equal([first.Id, second.Id], queue.Select(item => item.Id));
        Assert.Equal(2, competing.Count);
        Assert.Equal("Fan Two", queue[1].SubmitterDisplayName);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Approved_public_notes_show_only_the_newest_approved_date_and_source_with_credit(string implementation)
    {
        var repository = Create(implementation);
        var open = await repository.CreateAsync(New(picId: 101, kind: PhotoCorrectionKind.EventDate, proposed: "Open claim"));
        var older = await repository.CreateAsync(New(picId: 102, kind: PhotoCorrectionKind.EventDate, proposed: "1980", credit: "Old Credit"));
        await repository.ApproveAsync(older.Id, "ed@example.com", null, null);
        await Task.Delay(5);
        var newer = await repository.CreateAsync(New(picId: 102, kind: PhotoCorrectionKind.EventDate, proposed: "12 March 1980", member: otherMemberId, credit: "Fan Two"));
        await repository.ApproveAsync(newer.Id, "ed@example.com", null, null);
        var source = await repository.CreateAsync(New(picId: 102, kind: PhotoCorrectionKind.PhotographerSource, proposed: "Programme p.4"));
        await repository.ApproveAsync(source.Id, "ed@example.com", null, null);
        var caption = await repository.CreateAsync(New(picId: 102, kind: PhotoCorrectionKind.Caption, proposed: "New caption"));
        await repository.ApproveAsync(caption.Id, "ed@example.com", null, "Old caption");
        var rejected = await repository.CreateAsync(New(picId: 103, kind: PhotoCorrectionKind.EventDate));
        await repository.RejectAsync(rejected.Id, "ed@example.com", "No", null);

        var notes = await repository.GetApprovedPublicAsync(102);

        Assert.Equal(
            [
                new PublicPhotoCorrection(PhotoCorrectionKind.EventDate, "12 March 1980", "Fan Two"),
                new PublicPhotoCorrection(PhotoCorrectionKind.PhotographerSource, "Programme p.4", null),
            ],
            notes);
        Assert.Empty(await repository.GetApprovedPublicAsync(101));
        Assert.Empty(await repository.GetApprovedPublicAsync(103));

        await repository.RevertAsync(newer.Id, "ed@example.com", null);
        Assert.Equal("1980", (await repository.GetApprovedPublicAsync(102))[0].Value);
        Assert.NotEqual(open.Id, Guid.Empty);
    }

    [Fact]
    public async Task Ef_second_editor_deciding_the_same_proposal_loses_the_race()
    {
        var repository = new EfPhotoCorrectionRepository(dbContext);
        var created = await repository.CreateAsync(New(picId: 101));

        await using var staleContext = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        var staleEditor = new EfPhotoCorrectionRepository(staleContext);
        // The stale editor loads the proposal as Pending before the first editor decides.
        var stale = await staleContext.PhotoCorrectionProposals.SingleAsync(row => row.Id == created.Id);

        await repository.ApproveAsync(created.Id, "first@example.com", null, "Old");

        stale.Status = PhotoCorrectionStatus.Rejected;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
        Assert.Equal(PhotoCorrectionStatus.Approved, (await staleEditor.GetByIdAsync(created.Id))!.Status);
    }

    private IPhotoCorrectionRepository Create(string implementation) =>
        implementation == "ef"
            ? new EfPhotoCorrectionRepository(dbContext)
            : new InMemoryPhotoCorrectionRepository(id => dbContext.MemberAccounts.Find(id));

    private NewPhotoCorrectionProposal New(
        int picId,
        string kind = PhotoCorrectionKind.Caption,
        Guid? member = null,
        Guid? id = null,
        string? evidenceUrl = "https://example.com/ticket",
        string proposed = "Soundcheck, Wembley",
        string? credit = null) =>
        new(
            picId,
            member ?? memberId,
            kind,
            "Old caption",
            proposed,
            "I was there.",
            evidenceUrl,
            PhotoCorrectionEvidenceType.WebPage,
            PhotoCorrectionConfidence.Probable,
            credit,
            id);
}
