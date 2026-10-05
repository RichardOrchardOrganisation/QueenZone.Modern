namespace QueenZone.Data;

internal static class HomePollResultsBuilder
{
    public static HomePollResults Build(
        HomePollMetadata poll,
        IReadOnlyList<(Guid OptionId, string OptionText, int DisplayOrder)> options,
        IReadOnlyDictionary<Guid, int> optionCounts,
        Guid? selectedOptionId)
    {
        var totalVotes = optionCounts.Values.Sum();
        var isClosed = poll.ClosedAt is not null;
        var resultOptions = options
            .OrderBy(option => option.DisplayOrder)
            .ThenBy(option => option.OptionText)
            .Select(option =>
            {
                var count = optionCounts.GetValueOrDefault(option.OptionId);
                var percentage = totalVotes == 0 ? 0d : Math.Round(100d * count / totalVotes, 1);
                return new HomePollOptionResult(
                    option.OptionId,
                    option.OptionText,
                    option.DisplayOrder,
                    count,
                    percentage);
            })
            .ToList();

        return new HomePollResults(
            poll.Id,
            poll.Question,
            poll.ClosedAt,
            poll.CreatedAt,
            poll.PublishedAt,
            totalVotes,
            selectedOptionId is not null,
            selectedOptionId,
            isClosed,
            resultOptions);
    }
}

internal sealed record HomePollMetadata(Guid Id, string Question, DateTimeOffset? ClosedAt, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);
