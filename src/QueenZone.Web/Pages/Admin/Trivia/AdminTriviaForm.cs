using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Trivia;

public sealed class AdminTriviaForm
{
    [FromForm(Name = "text")]
    public string Text { get; init; } = string.Empty;

    [FromForm(Name = "category")]
    public string? Category { get; init; }

    [FromForm(Name = "difficulty")]
    public string? Difficulty { get; init; }

    [FromForm(Name = "source")]
    public string? Source { get; init; }

    [FromForm(Name = "isPublished")]
    public bool IsPublished { get; init; }

    public AdminTriviaDraft ToDraft() =>
        new(
            (Text ?? string.Empty).Trim(),
            IsPublished,
            TriviaValidation.NormalizeOptional(Category),
            TriviaValidation.NormalizeDifficulty(Difficulty),
            TriviaValidation.NormalizeOptional(Source));
}
