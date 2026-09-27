namespace QueenZone.Data;

public sealed record AdminFanPerformanceItem(
    int Id,
    string Title,
    string PerformedBy,
    string Description,
    string AudioFileName,
    long FileSizeBytes,
    DateTime DateAdded,
    bool IsVisible,
    int? DurationSeconds = null)
{
    public FanPerformance ToFanPerformance() =>
        new(Id, Title, PerformedBy, Description, AudioFileName, FileSizeBytes, DateAdded, DurationSeconds);

    public AdminFanPerformanceConcurrencyToken ToConcurrencyToken() =>
        new(Title, PerformedBy, Description, DateAdded, IsVisible);
}

public sealed record AdminFanPerformancePage(
    IReadOnlyList<AdminFanPerformanceItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record AdminFanPerformanceListFilter(
    bool? IsVisible = null,
    string? Search = null);

public sealed record AdminFanPerformanceCreateRequest(
    string Title,
    string PerformedBy,
    string Description,
    string AudioFileName,
    long FileSizeBytes,
    DateTime DateAdded,
    bool IsVisible,
    int? DurationSeconds = null);

public sealed record AdminFanPerformanceUpdateRequest(
    string Title,
    string PerformedBy,
    string Description,
    DateTime DateAdded);

public sealed record AdminFanPerformanceConcurrencyToken(
    string Title,
    string PerformedBy,
    string Description,
    DateTime DateAdded,
    bool IsVisible);
