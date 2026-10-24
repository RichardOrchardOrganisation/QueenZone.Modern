using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed record NewsDraftPreservedQuote(
    string Speaker,
    string ExactText,
    string SourceUrl,
    string? SourceContext);

public sealed record NewsDraftStructuredResult(
    string Title,
    string? Slug,
    string Excerpt,
    string Body,
    IReadOnlyList<string> RelatedEntities,
    IReadOnlyList<string> SourceUrls,
    IReadOnlyList<string> SourceNames,
    string? AttributionText,
    string? ConfidenceNotes,
    string? SourceNotes,
    DateTime? SuggestedPublishAt,
    bool SecondarySourceWarning,
    IReadOnlyList<NewsDraftPreservedQuote> PreservedQuotes);

public sealed record NewsDraftSourceAttribution(
    IReadOnlyList<string> SourceUrls,
    IReadOnlyList<string> SourceNames,
    string AttributionText,
    string SourceNotes,
    string ConfidenceNotes);

public sealed record NewsDraftRunOptions(
    bool DryRun = false,
    bool ForceRegenerate = false,
    int? PerRunCandidateLimit = null,
    bool BypassConfidenceThreshold = false);

public sealed record NewsDraftRunResult(
    int CandidatesConsidered,
    int DraftsCreated,
    int Skipped,
    int Failures,
    IReadOnlyList<string> Errors)
{
    public int CandidatesAttempted { get; init; } = CandidatesConsidered;
}

public sealed record NewsDraftCandidateResult(
    int CandidateId,
    int? DraftId,
    bool Succeeded,
    string? ErrorMessage)
{
    public bool Attempted { get; init; } = true;
}
