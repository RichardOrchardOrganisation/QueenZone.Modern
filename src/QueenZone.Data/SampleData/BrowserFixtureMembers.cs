using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Fixed member accounts the Testing host seeds when <c>MemberBrowserFixture:Enabled</c> is set,
/// so Deterministic browser specs can reach member profile and conversation pages (#2436).
/// Each spec uses its own pair so follow, block and message state never crosses between specs.
/// </summary>
public static class BrowserFixtureMembers
{
    public static readonly BrowserFixtureMember ProfileViewer =
        new(Guid.Parse("e2e0a000-0000-4000-8000-000000000001"), "E2E Profile Viewer");

    public static readonly BrowserFixtureMember ProfileSubject =
        new(Guid.Parse("e2e0a000-0000-4000-8000-000000000002"), "E2E Profile Subject");

    public static readonly BrowserFixtureMember ConversationSender =
        new(Guid.Parse("e2e0a000-0000-4000-8000-000000000003"), "E2E Conversation Sender");

    public static readonly BrowserFixtureMember ConversationRecipient =
        new(Guid.Parse("e2e0a000-0000-4000-8000-000000000004"), "E2E Conversation Recipient");

    public static readonly BrowserFixtureMember SettingsAvatarOwner =
        new(Guid.Parse("e2e0a000-0000-4000-8000-000000000005"), "E2E Settings Avatar Owner");

    public static IReadOnlyList<BrowserFixtureMember> All { get; } =
        [ProfileViewer, ProfileSubject, ConversationSender, ConversationRecipient, SettingsAvatarOwner];
}

public sealed record BrowserFixtureMember(Guid Id, string DisplayName)
{
    public string Email => $"{Id:N}@e2e.queenzone.local";

    public MemberAccount ToAccount() => new()
    {
        Id = Id,
        Email = Email,
        NormalizedEmail = Email.ToUpperInvariant(),
        DisplayName = DisplayName,
        // Old enough that the private message new-account rate limits never apply.
        CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        MessagePrivacy = MemberMessagePrivacy.Members,
    };
}
