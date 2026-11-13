using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public abstract class PhotoCorrectionRepositoryContractTests
{
    protected abstract IPhotoCorrectionRepository Repository { get; }
    protected abstract Task SeedMemberAsync(MemberAccount member);
    private const string Editor = "editor@example.test";

    private async Task<Guid> MemberAsync()
    {
        var id = Guid.NewGuid();
        await SeedMemberAsync(new MemberAccount
        {
            Id = id,
            DisplayName = "Fan",
            Email = $"{id}@example.test",
            NormalizedEmail = $"{id}@EXAMPLE.TEST",
            CreatedAt = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
        });
        return id;
    }

    private static NewPhotoCorrectionProposal Proposal(Guid member, int pic = 101, string kind = PhotoCorrectionKind.Caption,
        string value = "Wembley soundcheck", Guid? id = null) =>
        new(pic, member, kind, "Before", value, "Ticket evidence", "https://example.test/ticket",
            PhotoCorrectionEvidenceType.WebPage, PhotoCorrectionConfidence.Probable, "Fan", id);

    [Fact]
    public async Task Create_is_pending_idempotent_and_blocks_duplicate_open_fields_until_decided()
    {
        var member = await MemberAsync();
        var input = Proposal(member, id: Guid.NewGuid());
        var created = await Repository.CreateAsync(input);
        Assert.Equal(input.Id, created.Id);
        Assert.Equal(PhotoCorrectionStatus.Pending, created.Status);
        Assert.NotEqual(default, created.SubmittedAt);
        var loaded = (await Repository.GetByIdAsync(created.Id))!;
        Assert.Equal(input.ProposedValue, loaded.ProposedValue);
        Assert.Equal(member, loaded.SubmitterMemberId);
        Assert.Equal(101, loaded.PicId);
        Assert.Equal(input.EvidenceUrl, loaded.EvidenceUrl);
        Assert.Equal(created.Id, (await Repository.CreateAsync(input)).Id);
        Assert.Single(await Repository.GetForPhotoAsync(101));
        await Assert.ThrowsAsync<DuplicatePhotoCorrectionException>(() => Repository.CreateAsync(Proposal(member)));
        await Repository.RejectAsync(created.Id, Editor, "Not enough evidence", null);
        Assert.Equal(PhotoCorrectionStatus.Pending, (await Repository.CreateAsync(Proposal(member))).Status);
    }

    [Fact]
    public async Task Open_limit_is_per_member_and_decisions_free_capacity()
    {
        var member = await MemberAsync();
        var rows = new List<PhotoCorrectionProposal>();
        for (var i = 0; i < PhotoCorrectionValidation.MaxOpenPerMember; i++)
        {
            rows.Add(await Repository.CreateAsync(Proposal(member, i + 1)));
        }
        await Assert.ThrowsAsync<PhotoCorrectionLimitException>(() => Repository.CreateAsync(Proposal(member, 999)));
        Assert.Equal(PhotoCorrectionStatus.Pending, (await Repository.CreateAsync(Proposal(await MemberAsync(), 999))).Status);
        await Repository.RejectAsync(rows[0].Id, Editor, "No", null);
        Assert.Equal(PhotoCorrectionStatus.Pending, (await Repository.CreateAsync(Proposal(member, 999))).Status);
    }

    [Fact]
    public async Task Approve_records_reviewer_and_applied_value_and_only_approved_can_revert()
    {
        var member = await MemberAsync();
        var row = await Repository.CreateAsync(Proposal(member));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RevertAsync(row.Id, Editor, null));
        await Repository.ApproveAsync(row.Id, Editor, "Checked", "Previous caption");
        var approved = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(PhotoCorrectionStatus.Approved, approved.Status);
        Assert.Equal(Editor, approved.ReviewerEmail);
        Assert.Equal("Previous caption", approved.AppliedFromValue);
        Assert.NotNull(approved.AppliedAt);
        Assert.NotNull(approved.ReviewedAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ApproveAsync(row.Id, Editor, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(row.Id, Editor, "No", null));
        await Repository.RevertAsync(row.Id, Editor, "Wrong show");
        Assert.Equal(PhotoCorrectionStatus.Reverted, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RevertAsync(row.Id, Editor, null));
        var date = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.EventDate, value: "1986-07-12"));
        await Repository.ApproveAsync(date.Id, Editor, null, null);
        var unapplied = (await Repository.GetByIdAsync(date.Id))!;
        Assert.Null(unapplied.AppliedFromValue);
        Assert.Null(unapplied.AppliedAt);
    }

    [Fact]
    public async Task Request_info_and_owner_response_keep_proposal_open_then_withdraw_closes_it()
    {
        var member = await MemberAsync();
        var other = await MemberAsync();
        var row = await Repository.CreateAsync(Proposal(member));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RespondAsync(row.Id, member, "Too early"));
        await Repository.RequestInfoAsync(row.Id, Editor, "Which show?", "Internal");
        var asked = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(PhotoCorrectionStatus.NeedsInfo, asked.Status);
        Assert.Equal("Which show?", asked.MessageToMember);
        Assert.Equal(row.Id, Assert.Single(await Repository.GetOpenAsync(1, 10)).Id);
        Assert.Null(await Repository.RespondAsync(row.Id, other, "Not mine"));
        Assert.Null(await Repository.WithdrawAsync(row.Id, other));
        Assert.Equal(PhotoCorrectionStatus.NeedsInfo, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Repository.RespondAsync(row.Id, member, "Wembley, second night");
        var answered = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(PhotoCorrectionStatus.Pending, answered.Status);
        Assert.Equal("Wembley, second night", answered.MemberReply);
        await Repository.WithdrawAsync(row.Id, member);
        Assert.Equal(PhotoCorrectionStatus.Withdrawn, (await Repository.GetByIdAsync(row.Id))!.Status);
        Assert.Empty(await Repository.GetOpenAsync(1, 10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.WithdrawAsync(row.Id, member));
    }

    [Fact]
    public async Task Reject_requires_message_and_removes_proposal_from_open_queue()
    {
        var row = await Repository.CreateAsync(Proposal(await MemberAsync()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RejectAsync(row.Id, Editor, "  ", null));
        Assert.Equal(PhotoCorrectionStatus.Pending, (await Repository.GetByIdAsync(row.Id))!.Status);
        await Repository.RejectAsync(row.Id, Editor, "No source", null);
        var rejected = (await Repository.GetByIdAsync(row.Id))!;
        Assert.Equal(PhotoCorrectionStatus.Rejected, rejected.Status);
        Assert.Equal("No source", rejected.MessageToMember);
        Assert.Empty(await Repository.GetOpenAsync(1, 10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.ApproveAsync(row.Id, Editor, null, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Repository.RevertAsync(row.Id, Editor, null));
    }

    [Fact]
    public async Task Unknown_proposals_return_null_for_reads_and_mutations()
    {
        var id = Guid.NewGuid();
        var member = await MemberAsync();
        Assert.Null(await Repository.GetByIdAsync(id));
        Assert.Null(await Repository.ApproveAsync(id, Editor, null, null));
        Assert.Null(await Repository.RejectAsync(id, Editor, "No", null));
        Assert.Null(await Repository.RequestInfoAsync(id, Editor, "Which show?", null));
        Assert.Null(await Repository.RevertAsync(id, Editor, null));
        Assert.Null(await Repository.RespondAsync(id, member, "Reply"));
        Assert.Null(await Repository.WithdrawAsync(id, member));
    }

    [Fact]
    public async Task Queues_separate_memories_and_order_competing_and_member_proposals()
    {
        var member = await MemberAsync();
        var first = await Repository.CreateAsync(Proposal(member));
        var second = await Repository.CreateAsync(Proposal(await MemberAsync()));
        var third = await Repository.CreateAsync(Proposal(member, 102));
        var memory = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.Memory));
        var corrections = new[] { first, second, third };
        var oldest = corrections.OrderBy(p => p.SubmittedAt).ThenBy(p => p.Id).Select(p => p.Id).ToArray();
        Assert.Equal(oldest, (await Repository.GetOpenAsync(1, 10)).Select(p => p.Id));
        Assert.Equal(oldest[1], Assert.Single(await Repository.GetOpenAsync(2, 1)).Id);
        Assert.Equal(memory.Id, Assert.Single(await Repository.GetOpenAsync(1, 10, memories: true)).Id);
        Assert.Equal(new[] { first, second, memory }.OrderByDescending(p => p.SubmittedAt).ThenBy(p => p.Id).Select(p => p.Id),
            (await Repository.GetForPhotoAsync(101)).Select(p => p.Id));
        var mine = new[] { first, third, memory }.OrderByDescending(p => p.SubmittedAt).ThenBy(p => p.Id).Select(p => p.Id).ToArray();
        var page = await Repository.GetBySubmitterAsync(member, 1, 2);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(mine.Take(2), page.Items.Select(p => p.Id));
        Assert.Equal(mine[2], Assert.Single((await Repository.GetBySubmitterAsync(member, 2, 2)).Items).Id);
        Assert.Empty((await Repository.GetBySubmitterAsync(Guid.NewGuid())).Items);
    }

    [Fact]
    public async Task Public_notes_include_approved_date_and_source_but_exclude_other_states_and_captions()
    {
        var member = await MemberAsync();
        var date = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.EventDate, value: "1986-07-12"));
        var source = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.PhotographerSource, value: "Programme page 4"));
        var caption = await Repository.CreateAsync(Proposal(member));
        var rejected = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.PhotoDate, value: "1986"));
        await Repository.RejectAsync(rejected.Id, Editor, "No evidence", null);
        await Repository.CreateAsync(Proposal(await MemberAsync(), kind: PhotoCorrectionKind.EventDate, value: "1985"));
        Assert.Empty(await Repository.GetApprovedPublicAsync(101));
        foreach (var row in new[] { date, source, caption })
        {
            await Repository.ApproveAsync(row.Id, Editor, null, null);
        }
        Assert.Equal([
            new PublicPhotoCorrection(PhotoCorrectionKind.EventDate, "1986-07-12", "Fan"),
            new PublicPhotoCorrection(PhotoCorrectionKind.PhotographerSource, "Programme page 4", "Fan")],
            await Repository.GetApprovedPublicAsync(101));
        await Repository.RevertAsync(date.Id, Editor, null);
        Assert.Equal(PhotoCorrectionKind.PhotographerSource, Assert.Single(await Repository.GetApprovedPublicAsync(101)).Kind);
    }

    [Fact]
    public async Task Public_memories_are_approved_only_newest_first_and_capped()
    {
        var member = await MemberAsync();
        var approved = new List<PhotoCorrectionProposal>();
        for (var i = 0; i < PhotoCorrectionValidation.MaxPublicMemories + 2; i++)
        {
            var row = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.Memory, value: $"Memory {i}"));
            approved.Add((await Repository.ApproveAsync(row.Id, Editor, null, null))!);
        }
        var open = await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.Memory, value: "Open"));
        await Repository.RejectAsync(open.Id, Editor, "No", null);
        await Repository.CreateAsync(Proposal(member, kind: PhotoCorrectionKind.Memory, value: "Still open"));
        var expected = approved.OrderByDescending(p => p.ReviewedAt).ThenBy(p => p.Id).Take(PhotoCorrectionValidation.MaxPublicMemories);
        Assert.Equal(expected.Select(p => p.ProposedValue), (await Repository.GetApprovedMemoriesAsync(101)).Select(p => p.Value));
        Assert.Empty(await Repository.GetApprovedPublicAsync(101));
        foreach (var row in approved)
        {
            await Repository.RevertAsync(row.Id, Editor, null);
        }
        Assert.Empty(await Repository.GetApprovedMemoriesAsync(101));
    }
}

public sealed class InMemoryPhotoCorrectionRepositoryContractTests : PhotoCorrectionRepositoryContractTests
{
    private readonly Dictionary<Guid, MemberAccount> members = [];
    protected override IPhotoCorrectionRepository Repository { get; }
    public InMemoryPhotoCorrectionRepositoryContractTests() => Repository = new InMemoryPhotoCorrectionRepository(id => members.GetValueOrDefault(id));
    protected override Task SeedMemberAsync(MemberAccount member)
    {
        members.Add(member.Id, member);
        return Task.CompletedTask;
    }
}

public sealed class EfPhotoCorrectionRepositoryContractTests : PhotoCorrectionRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext db;
    protected override IPhotoCorrectionRepository Repository { get; }
    public EfPhotoCorrectionRepositoryContractTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        Repository = new EfPhotoCorrectionRepository(db);
    }
    protected override async Task SeedMemberAsync(MemberAccount member)
    {
        db.MemberAccounts.Add(member);
        await db.SaveChangesAsync();
    }
    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }
}
