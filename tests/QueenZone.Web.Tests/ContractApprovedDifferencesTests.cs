using QueenZone.TestUtilities;

namespace QueenZone.Web.Tests;

public sealed class ContractApprovedDifferencesTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"contract-{Guid.NewGuid():N}.json");
    private const string Listed = """
        [{"id":"format","suite":"Messages","member":"Inbox","runners":["Sql"],
          "kind":"format","reason":"Intentional representation in this fixture","owner":"test"}]
        """;

    [Fact]
    public void Unlisted_difference_fails()
    {
        File.WriteAllText(path, "[]");
        var catalog = ApprovedDifferenceCatalog.Load(path);
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Verify("Messages", "Inbox", "Sql", ["format"]));
        Assert.Contains("Unlisted", exception.Message);
    }

    [Fact]
    public void Unobserved_entry_is_stale()
    {
        File.WriteAllText(path, Listed);
        var catalog = ApprovedDifferenceCatalog.Load(path);
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Verify("Messages", "Inbox", "Sql", []));
        Assert.Contains("Stale", exception.Message);
    }

    [Fact]
    public void Matching_entry_with_reason_is_allowed()
    {
        File.WriteAllText(path, Listed);
        ApprovedDifferenceCatalog.Load(path).Verify("Messages", "Inbox", "Sql", ["format"]);
    }

    [Theory]
    [InlineData("Messages", "Inbox", "Memory")]
    [InlineData("Other", "Inbox", "Sql")]
    [InlineData("Messages", "Other", "Sql")]
    public void Different_scope_is_neither_stale_nor_an_approval(string suite, string member, string runner)
    {
        File.WriteAllText(path, Listed);
        var catalog = ApprovedDifferenceCatalog.Load(path);
        catalog.Verify(suite, member, runner, []);
        Assert.Throws<InvalidOperationException>(() => catalog.Verify(suite, member, runner, ["format"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Missing_or_empty_reason_is_rejected(string? reason)
    {
        var entry = new { id = "format", suite = "Messages", member = "Inbox", runners = new[] { "Sql" }, kind = "format", reason, owner = "test" };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { entry },
            new System.Text.Json.JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
        Assert.Throws<InvalidOperationException>(() => ApprovedDifferenceCatalog.Load(path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    public void Invalid_catalog_is_rejected(string json)
    {
        File.WriteAllText(path, json);
        Assert.ThrowsAny<Exception>(() => ApprovedDifferenceCatalog.Load(path));
    }

    public void Dispose() => File.Delete(path);
}
