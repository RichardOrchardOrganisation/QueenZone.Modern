namespace QueenZone.Data;

/// <summary>What a member proposes to change about an existing archive photograph.</summary>
public static class PhotoCorrectionKind
{
    public const string Caption = "Caption";

    public const string EventDate = "EventDate";

    public const string PhotographerSource = "PhotographerSource";

    /// <summary>A first-person recollection. Never treated as fact and never changes the photo.</summary>
    public const string Memory = "Memory";

    public static readonly IReadOnlyList<string> All = [Caption, EventDate, PhotographerSource, Memory];

    public static bool IsKnown(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && All.Contains(kind.Trim(), StringComparer.Ordinal);

    public static string Label(string kind) => kind switch
    {
        Caption => "Caption",
        EventDate => "Event date",
        PhotographerSource => "Photographer or source",
        Memory => "Memory",
        _ => kind,
    };
}

/// <summary>Kind of evidence a member points to; the link itself is optional.</summary>
public static class PhotoCorrectionEvidenceType
{
    public const string WebPage = "WebPage";

    public const string Publication = "Publication";

    public const string PersonalRecord = "PersonalRecord";

    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [WebPage, Publication, PersonalRecord, Other];

    public static bool IsKnown(string? type) =>
        !string.IsNullOrWhiteSpace(type) && All.Contains(type.Trim(), StringComparer.Ordinal);
}

/// <summary>How sure the proposer is; shown to editors so uncertain claims are not treated as fact.</summary>
public static class PhotoCorrectionConfidence
{
    public const string Certain = "Certain";

    public const string Probable = "Probable";

    public const string Unsure = "Unsure";

    public static readonly IReadOnlyList<string> All = [Certain, Probable, Unsure];

    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) && All.Contains(value.Trim(), StringComparer.Ordinal);
}

public static class PhotoCorrectionStatus
{
    public const string Pending = "Pending";

    public const string NeedsInfo = "NeedsInfo";

    public const string Approved = "Approved";

    public const string Rejected = "Rejected";

    public const string Reverted = "Reverted";

    public const string Withdrawn = "Withdrawn";

    public static readonly IReadOnlyList<string> All = [Pending, NeedsInfo, Approved, Rejected, Reverted, Withdrawn];

    internal static readonly SubmissionStatusSet Statuses = new("photo correction", All);

    public static bool IsKnown(string? status) => Statuses.IsKnown(status);

    /// <summary>Open proposals still waiting on an editor decision.</summary>
    public static bool IsOpen(string status) =>
        string.Equals(status, Pending, StringComparison.Ordinal)
        || string.Equals(status, NeedsInfo, StringComparison.Ordinal);

    public static string Normalize(string status) => Statuses.Normalize(status);
}

public static class PhotoCorrectionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PhotoCorrectionStatus.Pending] =
            [
                PhotoCorrectionStatus.NeedsInfo,
                PhotoCorrectionStatus.Approved,
                PhotoCorrectionStatus.Rejected,
                PhotoCorrectionStatus.Withdrawn,
            ],
            [PhotoCorrectionStatus.NeedsInfo] =
            [
                PhotoCorrectionStatus.Pending,
                PhotoCorrectionStatus.Approved,
                PhotoCorrectionStatus.Rejected,
                PhotoCorrectionStatus.Withdrawn,
            ],
            [PhotoCorrectionStatus.Approved] = [PhotoCorrectionStatus.Reverted],
            [PhotoCorrectionStatus.Rejected] = [],
            [PhotoCorrectionStatus.Reverted] = [],
            [PhotoCorrectionStatus.Withdrawn] = [],
        };

    private static readonly SubmissionWorkflowRules Rules =
        new(PhotoCorrectionStatus.Statuses, AllowedTransitions);

    public static bool CanTransition(string current, string next) => Rules.CanTransition(current, next);

    public static bool TryValidateStatusChange(string current, string next, out string? error) =>
        Rules.TryValidateStatusChange(current, next, out error);
}

public sealed record PhotoCorrectionProposal(
    Guid Id,
    int PicId,
    Guid SubmitterMemberId,
    string Kind,
    string? CurrentValue,
    string ProposedValue,
    string Explanation,
    string? EvidenceUrl,
    string? EvidenceType,
    string Confidence,
    string? PublicCreditName,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewerEmail,
    string? ReviewNotes,
    string? MessageToMember,
    string? AppliedFromValue,
    DateTimeOffset? AppliedAt,
    string? SubmitterDisplayName = null,
    string? MemberReply = null);

public sealed record PhotoCorrectionListItem(
    Guid Id,
    int PicId,
    string Kind,
    string ProposedValue,
    string SubmitterDisplayName,
    DateTimeOffset SubmittedAt,
    string Status);

public sealed record NewPhotoCorrectionProposal(
    int PicId,
    Guid SubmitterMemberId,
    string Kind,
    string? CurrentValue,
    string ProposedValue,
    string Explanation,
    string? EvidenceUrl,
    string? EvidenceType,
    string Confidence,
    string? PublicCreditName,
    Guid? Id = null);

public sealed record PhotoCorrectionAuditEntry(
    long Id,
    string Action,
    string ActorEmail,
    DateTimeOffset OccurredAt,
    string? Details);

/// <summary>Thrown when a member already has an open proposal for the same photo and field.</summary>
public sealed class DuplicatePhotoCorrectionException : InvalidOperationException
{
    public const string UserMessage =
        "You already have an open proposal for this photo and field. Wait for the review before sending another.";

    public DuplicatePhotoCorrectionException()
        : base(UserMessage)
    {
    }
}

/// <summary>Thrown when a member has too many proposals waiting for review.</summary>
public sealed class PhotoCorrectionLimitException(int limit)
    : InvalidOperationException(
        $"You have {limit} proposals waiting for review. Wait for some to be reviewed before sending more.");

public static partial class PhotoCorrectionValidation
{
    public const int MaxValueLength = 500;

    /// <summary>Gallery captions live in PIC_FILES_T.Name, which is 200 characters wide.</summary>
    public const int MaxCaptionLength = 200;

    public const int MaxExplanationLength = 1000;

    public const int MaxEvidenceUrlLength = 500;

    public const int MaxCreditNameLength = 100;

    public const int MaxMessageLength = 500;

    /// <summary>Approved fan memories shown on one photo page.</summary>
    public const int MaxPublicMemories = 5;

    /// <summary>Open proposals one member may have at once, across all photos.</summary>
    public const int MaxOpenPerMember = 10;

    public static IReadOnlyList<string> Validate(
        string kind,
        string proposedValue,
        string explanation,
        string? evidenceUrl,
        string? evidenceType,
        string confidence,
        string? publicCreditName)
    {
        var errors = new List<string>();
        if (!PhotoCorrectionKind.IsKnown(kind))
        {
            errors.Add("Choose what you want to correct.");
        }

        if (string.IsNullOrWhiteSpace(proposedValue))
        {
            errors.Add("Enter the corrected value.");
        }
        else if (proposedValue.Trim().Length > MaxValueLength)
        {
            errors.Add($"The corrected value must be {MaxValueLength} characters or fewer.");
        }
        else if (kind == PhotoCorrectionKind.Caption && proposedValue.Trim().Length > MaxCaptionLength)
        {
            errors.Add($"A caption must be {MaxCaptionLength} characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(explanation) && kind != PhotoCorrectionKind.Memory)
        {
            errors.Add("Explain how you know, so an editor can check it.");
        }
        else if (explanation.Trim().Length > MaxExplanationLength)
        {
            errors.Add($"The explanation must be {MaxExplanationLength} characters or fewer.");
        }

        if (!PhotoCorrectionConfidence.IsKnown(confidence))
        {
            errors.Add("Say how sure you are.");
        }

        if (!string.IsNullOrWhiteSpace(evidenceType) && !PhotoCorrectionEvidenceType.IsKnown(evidenceType))
        {
            errors.Add("Choose a valid evidence type.");
        }

        if (!string.IsNullOrWhiteSpace(evidenceUrl) && !IsSafeEvidenceUrl(evidenceUrl))
        {
            errors.Add("The evidence link must be a full http:// or https:// web address without a username or password.");
        }

        if (!string.IsNullOrWhiteSpace(publicCreditName) && publicCreditName.Trim().Length > MaxCreditNameLength)
        {
            errors.Add($"The credit name must be {MaxCreditNameLength} characters or fewer.");
        }

        if (ContainsContactDetails(proposedValue) || ContainsContactDetails(explanation))
        {
            errors.Add("Please remove email addresses and phone numbers; editors never need them here.");
        }

        return errors;
    }

    /// <summary>
    /// Free text is reviewed by editors and, for memories, may be published. Refuse obvious
    /// contact details (emails, long digit runs such as phone numbers) so they never get stored.
    /// </summary>
    public static bool ContainsContactDetails(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return EmailPattern().IsMatch(text)
            || PhonePattern().Matches(text).Any(match => match.Value.Count(char.IsDigit) >= 9);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+", System.Text.RegularExpressions.RegexOptions.None, 250)]
    private static partial System.Text.RegularExpressions.Regex EmailPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"\+?\d[\d\s().\-]{7,}\d", System.Text.RegularExpressions.RegexOptions.None, 250)]
    private static partial System.Text.RegularExpressions.Regex PhonePattern();

    /// <summary>
    /// Evidence links are shown to editors only, as plain text. Anything but a plain
    /// http(s) URL is refused so a link can never be a script or credential carrier.
    /// </summary>
    public static bool IsSafeEvidenceUrl(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MaxEvidenceUrlLength
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && !trimmed.Any(char.IsControl);
    }
}

/// <summary>
/// The only part of an approved proposal that may be shown publicly: the accepted value and
/// the credit the member chose. Evidence, notes and identity are deliberately absent.
/// </summary>
public sealed record PublicPhotoCorrection(string Kind, string Value, string? CreditName);
