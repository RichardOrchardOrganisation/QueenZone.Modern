using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoCorrectionValidationTests
{
    [Theory]
    [InlineData("https://example.com/page", true)]
    [InlineData("http://example.com/page?a=1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("ftp://example.com/file", false)]
    [InlineData("https://user:secret@example.com/", false)]
    [InlineData("/relative/path", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void IsSafeEvidenceUrl_accepts_only_plain_http_links(string value, bool expected) =>
        Assert.Equal(expected, PhotoCorrectionValidation.IsSafeEvidenceUrl(value));

    [Fact]
    public void IsSafeEvidenceUrl_rejects_overlong_links() =>
        Assert.False(PhotoCorrectionValidation.IsSafeEvidenceUrl(
            "https://example.com/" + new string('a', PhotoCorrectionValidation.MaxEvidenceUrlLength)));

    [Fact]
    public void Validate_accepts_a_complete_proposal() =>
        Assert.Empty(Validate());

    [Fact]
    public void Validate_reports_every_missing_required_field()
    {
        var errors = PhotoCorrectionValidation.Validate("Nonsense", " ", " ", null, null, "Maybe", null);

        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Validate_rejects_captions_longer_than_the_gallery_column() =>
        Assert.Contains(
            "A caption must be 200 characters or fewer.",
            Validate(proposed: new string('x', PhotoCorrectionValidation.MaxCaptionLength + 1)));

    [Fact]
    public void Validate_allows_long_values_for_other_fields() =>
        Assert.Empty(Validate(
            kind: PhotoCorrectionKind.PhotographerSource,
            proposed: new string('x', PhotoCorrectionValidation.MaxCaptionLength + 1)));

    [Fact]
    public void Validate_rejects_unsafe_evidence_and_unknown_evidence_type()
    {
        var errors = Validate(evidenceUrl: "javascript:alert(1)", evidenceType: "Rumour");

        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData(PhotoCorrectionStatus.Pending, PhotoCorrectionStatus.Approved, true)]
    [InlineData(PhotoCorrectionStatus.Pending, PhotoCorrectionStatus.NeedsInfo, true)]
    [InlineData(PhotoCorrectionStatus.NeedsInfo, PhotoCorrectionStatus.Rejected, true)]
    [InlineData(PhotoCorrectionStatus.Approved, PhotoCorrectionStatus.Reverted, true)]
    [InlineData(PhotoCorrectionStatus.Pending, PhotoCorrectionStatus.Reverted, false)]
    [InlineData(PhotoCorrectionStatus.NeedsInfo, PhotoCorrectionStatus.Pending, false)]
    [InlineData(PhotoCorrectionStatus.Rejected, PhotoCorrectionStatus.Approved, false)]
    [InlineData(PhotoCorrectionStatus.Reverted, PhotoCorrectionStatus.Approved, false)]
    [InlineData("Bogus", PhotoCorrectionStatus.Approved, false)]
    public void Workflow_allows_only_reviewed_transitions(string from, string to, bool expected) =>
        Assert.Equal(expected, PhotoCorrectionWorkflow.CanTransition(from, to));

    [Fact]
    public void Status_and_kind_helpers_cover_known_values()
    {
        Assert.True(PhotoCorrectionStatus.IsOpen(PhotoCorrectionStatus.NeedsInfo));
        Assert.False(PhotoCorrectionStatus.IsOpen(PhotoCorrectionStatus.Approved));
        Assert.True(PhotoCorrectionStatus.IsKnown("pending"));
        Assert.Equal(PhotoCorrectionStatus.Pending, PhotoCorrectionStatus.Normalize("pending"));
        Assert.Equal("Event date", PhotoCorrectionKind.Label(PhotoCorrectionKind.EventDate));
        Assert.Equal("Photographer or source", PhotoCorrectionKind.Label(PhotoCorrectionKind.PhotographerSource));
        Assert.Equal("Caption", PhotoCorrectionKind.Label(PhotoCorrectionKind.Caption));
        Assert.Equal("Other", PhotoCorrectionKind.Label("Other"));
        Assert.False(PhotoCorrectionKind.IsKnown(null));
    }

    private static IReadOnlyList<string> Validate(
        string kind = PhotoCorrectionKind.Caption,
        string proposed = "Soundcheck, Wembley",
        string? evidenceUrl = null,
        string? evidenceType = null) =>
        PhotoCorrectionValidation.Validate(
            kind,
            proposed,
            "I was there.",
            evidenceUrl,
            evidenceType,
            PhotoCorrectionConfidence.Certain,
            null);
}
