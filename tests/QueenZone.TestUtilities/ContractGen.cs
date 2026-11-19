using CsCheck;

namespace QueenZone.TestUtilities;

public static class ContractGen
{
    public static Gen<string> Text { get; } = Gen.OneOf(
        "Queen", "QUEEN", "queen", "I", "i", "İ", "ı", "Café", "Cafe\u0301", "🎸👑");

    public static Gen<string?> Body { get; } = Gen.OneOf(
        Text.Select(text => (string?)$"  {text}  "),
        Gen.OneOf<string?>(null, "", " \t\r\n ", new string('x', 201) + "🎸"));

    public static Gen<int> PageSize { get; } = Gen.OneOf(0, 1, 2, 3, 10, 25, 99, 100, 101, 199, 200, 201);

    public static Gen<int?> ConversationPage { get; } = Gen.OneOf<int?>(null, -2, 0, 1, 2, 3, 99);

    public static Gen<DateTimeOffset> Timestamp { get; } =
        from minutes in Gen.Int[-2, 2]
        from offsetHours in Gen.OneOf(-8, 0, 3, 8)
        select new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero)
            .AddMinutes(minutes).ToOffset(TimeSpan.FromHours(offsetHours));

    public static Gen<(DateTimeOffset First, DateTimeOffset Tied)> TiedTimestamps { get; } =
        Timestamp.Select(value => (value, value.ToOffset(value.Offset == TimeSpan.Zero
            ? TimeSpan.FromHours(3) : TimeSpan.Zero)));
}
