using CsCheck;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.TestUtilities;

namespace QueenZone.ContractTests;

internal static class PrivateMessageContractProperty
{
    private sealed record Input(string Name, string OtherName, string? Body, string? Reply,
        DateTimeOffset SentAt, DateTimeOffset TiedAt, int? Page, int PageSize, int Visibility);

    private sealed record Message(int Order, Guid Sender, string Body, DateTimeOffset SentAt);

    private sealed class Conversation(Guid id, Guid left, Guid right)
    {
        public Guid Id { get; } = id;
        public Guid Left { get; } = left;
        public Guid Right { get; } = right;
        public List<Message> Messages { get; } = [];
        public HashSet<Guid> Archived { get; } = [];
        public HashSet<Guid> Removed { get; } = [];
        public bool Contains(Guid member) => member == Left || member == Right;
        public Guid Other(Guid member) => member == Left ? Right : Left;
    }

    private static readonly Gen<Input> Inputs =
        from name in ContractGen.Text
        from otherName in ContractGen.Text
        from body in ContractGen.Body
        from reply in ContractGen.Body
        from times in ContractGen.TiedTimestamps
        from page in ContractGen.ConversationPage
        from pageSize in ContractGen.PageSize
        from visibility in Gen.Int[0, 2]
        select new Input(name, otherName, body, reply, times.First, times.Tied, page, pageSize, visibility);

    public static Task RunAsync(IPrivateMessageRepository repository, Func<MemberAccount, Task> addMember,
        int iterations = 20) =>
        PropertyRun.RunAsync("PrivateMessage LINQ contract", Inputs,
            input => CheckAsync(repository, addMember, input), iter: iterations);

    private static async Task CheckAsync(IPrivateMessageRepository repository,
        Func<MemberAccount, Task> addMember, Input input)
    {
        var members = new[] { input.Name, input.OtherName, "İ Cafe\u0301 🎸", "ı Café 👑" }
            .Select(name => NewMember(name)).ToArray();
        foreach (var member in members)
        {
            await addMember(member);
        }

        var names = members.ToDictionary(member => member.Id, member => member.DisplayName);
        var conversations = new List<Conversation>();
        var order = 0;
        foreach (var (left, right) in new[] { (0, 1), (0, 2), (1, 2) })
        {
            var sent = await repository.SendNewOrExistingAsync(members[left].Id, members[right].Id,
                input.Body!, input.SentAt);
            Assert.True(sent.Succeeded, sent.ErrorMessage);
            Assert.NotNull(sent.ConversationId);
            var conversation = new Conversation(sent.ConversationId.Value, members[left].Id, members[right].Id);
            conversation.Messages.Add(new Message(++order, members[left].Id, Normalize(input.Body), input.SentAt));
            conversations.Add(conversation);
        }

        var first = conversations[0];
        // Insert time moves forward, ties at a different offset, then moves backwards.
        foreach (var sentAt in new[] { input.SentAt.AddMinutes(1), input.TiedAt.AddMinutes(1), input.SentAt.AddMinutes(-1), input.SentAt })
        {
            var sender = order % 2 == 0 ? first.Left : first.Right;
            var reply = await repository.ReplyAsync(first.Id, sender, input.Reply!, sentAt);
            Assert.True(reply.Succeeded, reply.ErrorMessage);
            Assert.Equal(first.Id, reply.ConversationId);
            first.Messages.Add(new Message(++order, sender, Normalize(input.Reply), sentAt));
        }

        await ApplyVisibilityAsync(repository, conversations[1], members[0].Id, input.Visibility);
        var identities = new Dictionary<int, (Guid Id, long SortKey)>();
        foreach (var conversation in conversations)
        {
            await CheckConversationAsync(repository, conversation, conversation.Left, names, 1, 100, identities);
            await CheckConversationAsync(repository, conversation, conversation.Left, names, input.Page, input.PageSize, identities);
            await CheckConversationAsync(repository, conversation, conversation.Right, names, input.Page, input.PageSize, identities);
            Assert.Null(await repository.GetConversationAsync(conversation.Id, members[3].Id, input.Page, input.PageSize));
        }

        var sortedKeys = identities.OrderBy(pair => pair.Key).Select(pair => pair.Value.SortKey).ToArray();
        Assert.Equal(sortedKeys.OrderBy(key => key), sortedKeys);
        Assert.Equal(order, sortedKeys.Distinct().Count());
        Assert.Equal(order, identities.Values.Select(value => value.Id).Distinct().Count());
        foreach (var member in members)
        {
            await CheckInboxAsync(repository, conversations, member.Id, names, input.Page ?? 0, input.PageSize, false);
            await CheckInboxAsync(repository, conversations, member.Id, names, input.Page ?? 0, input.PageSize, true);
        }
    }

    private static MemberAccount NewMember(string name)
    {
        var id = Guid.NewGuid();
        var email = $"contract-{id:N}@example.test";
        return new MemberAccount
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = name,
            CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    private static string Normalize(string? body) => string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();

    private static async Task ApplyVisibilityAsync(IPrivateMessageRepository repository,
        Conversation conversation, Guid member, int visibility)
    {
        if (visibility == 1)
        {
            Assert.True(await repository.ArchiveConversationAsync(conversation.Id, member));
            conversation.Archived.Add(member);
        }
        else if (visibility == 2)
        {
            Assert.True(await repository.RemoveConversationAsync(conversation.Id, member));
            conversation.Removed.Add(member);
        }
    }

    private static async Task CheckInboxAsync(IPrivateMessageRepository repository,
        List<Conversation> model, Guid member, Dictionary<Guid, string> names, int requestedPage,
        int requestedSize, bool archived)
    {
        var size = Math.Clamp(requestedSize, 1, PrivateMessageLimits.MaxInboxPageSize);
        var visible = model.Where(conversation => conversation.Contains(member)
                && conversation.Archived.Contains(member) == archived && !conversation.Removed.Contains(member))
            .OrderByDescending(conversation => conversation.Messages.Max(message => message.Order))
            .ThenByDescending(conversation => conversation.Id).ToArray();
        var pages = Math.Max(1, (visible.Length + size - 1) / size);
        var page = Math.Min(Math.Max(1, requestedPage), pages);
        var expected = visible.Skip((page - 1) * size).Take(size).ToArray();
        var actual = archived
            ? await repository.GetArchivedInboxAsync(member, requestedPage, requestedSize)
            : await repository.GetInboxAsync(member, requestedPage, requestedSize);
        Assert.Equal((visible.Length, page, size, pages), (actual.TotalCount, actual.Page, actual.PageSize, actual.TotalPages));
        Assert.Equal(expected.Select(conversation => conversation.Id), actual.Items.Select(item => item.ConversationId));
        foreach (var (conversation, item) in expected.Zip(actual.Items))
        {
            var body = conversation.Messages.OrderBy(message => message.Order).Last().Body;
            var preview = body[..Math.Min(body.Length, PrivateMessageLimits.PreviewLength)];
            var lastAt = conversation.Messages.OrderBy(message => message.Order)
                .Aggregate(conversation.Messages[0].SentAt, (latest, message) => message.SentAt > latest ? message.SentAt : latest);
            Assert.Equal(conversation.Other(member), item.OtherParticipantId);
            Assert.Equal(names[conversation.Other(member)], item.OtherParticipantDisplayName);
            Assert.Equal(preview, item.LastMessagePreview);
            Assert.Equal(lastAt, item.LastMessageAt);
        }
    }

    private static async Task CheckConversationAsync(IPrivateMessageRepository repository,
        Conversation conversation, Guid viewer, Dictionary<Guid, string> names, int? requestedPage,
        int requestedSize, Dictionary<int, (Guid Id, long SortKey)> identities)
    {
        var size = Math.Clamp(requestedSize, 1, PrivateMessageLimits.MaxConversationPageSize);
        var ordered = conversation.Messages.OrderBy(message => message.Order).ToArray();
        var pages = Math.Max(1, (ordered.Length + size - 1) / size);
        var page = requestedPage is null or < 1 ? pages : Math.Min(requestedPage.Value, pages);
        var expected = page == pages ? ordered.TakeLast(size).ToArray() : ordered.Skip((page - 1) * size).Take(size).ToArray();
        var actual = await repository.GetConversationAsync(conversation.Id, viewer, requestedPage, requestedSize);
        Assert.NotNull(actual);
        Assert.Equal(conversation.Id, actual.ConversationId);
        Assert.Equal(conversation.Other(viewer), actual.OtherParticipantId);
        Assert.Equal(names[conversation.Other(viewer)], actual.OtherParticipantDisplayName);
        Assert.Equal((ordered.Length, page, size, pages), (actual.TotalCount, actual.Page, actual.PageSize, actual.TotalPages));
        Assert.Equal(expected.Length, actual.Messages.Count);
        foreach (var (message, item) in expected.Zip(actual.Messages))
        {
            Assert.Equal(message.Sender, item.SenderMemberId);
            Assert.Equal(names[message.Sender], item.SenderDisplayName);
            Assert.Equal(message.Body, item.Body);
            Assert.Equal(message.SentAt, item.CreatedAt);
            Assert.Equal(message.Sender == viewer, item.IsMine);
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.True(item.SortKey > 0);
            // Generated IDs and provider-assigned keys are checked for stability, never used to predict content.
            if (identities.TryGetValue(message.Order, out var identity))
            {
                Assert.Equal(identity, (item.Id, item.SortKey));
            }
            else
            {
                identities.Add(message.Order, (item.Id, item.SortKey));
            }
        }
    }
}
