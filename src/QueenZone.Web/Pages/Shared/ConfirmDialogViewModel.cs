namespace QueenZone.Web.Pages;

/// <summary>
/// Trigger button plus styled confirm dialog rendered by <c>_ConfirmDialog</c> inside an existing form.
/// Replaces inline <c>onsubmit="return confirm(...)"</c>, which the site CSP blocks.
/// </summary>
public sealed record ConfirmDialogViewModel(
    string TriggerLabel,
    string Question,
    string? Detail = null,
    string? TriggerClass = null,
    string? ConfirmLabel = null,
    bool IsDanger = true);
