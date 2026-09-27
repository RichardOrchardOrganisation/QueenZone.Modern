using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestCaseOrderer("QueenZone.Web.Tests.SeededRandomTestCaseOrderer", "QueenZone.Web.Tests")]

namespace QueenZone.Web.Tests;

/// <summary>
/// Optional seeded shuffle of tests inside a class. Enabled when
/// <see cref="EnvironmentVariableName"/> is set to an integer seed (printed to stdout).
/// Unset, tests keep xUnit's default discovery order.
/// </summary>
public sealed class SeededRandomTestCaseOrderer : ITestCaseOrderer
{
    public const string EnvironmentVariableName = "QUEENZONE_WEB_TESTS_ORDER_SEED";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var items = testCases.ToList();
        var seedText = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        var ordered = SeededRandomOrder.Apply(items, seedText, out var printedSeed);
        if (printedSeed is not null)
        {
            Console.WriteLine($"QueenZone.Web.Tests random order seed: {printedSeed}");
        }

        return ordered;
    }
}

public static class SeededRandomOrder
{
    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> items, string? seedText, out string? printedSeed)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items as IList<T> ?? items.ToList();
        printedSeed = null;
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return list is IReadOnlyList<T> readOnly ? readOnly : list.ToList();
        }

        if (!int.TryParse(seedText, out var seed))
        {
            throw new InvalidOperationException(
                $"{SeededRandomTestCaseOrderer.EnvironmentVariableName} must be an integer seed, got '{seedText}'.");
        }

        printedSeed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rng = new Random(seed);
        return list.OrderBy(_ => rng.Next()).ToList();
    }
}
