using QueenZone.Data;
using QueenZone.Web.Pages.Admin.Timeline;

namespace QueenZone.Web.Tests;

public sealed class AdminTimelineFormTests
{
    [Fact]
    public void Defaults_are_empty_fields_and_unpublished()
    {
        var form = new AdminTimelineForm();

        Assert.Equal(string.Empty, form.Title);
        Assert.Equal(string.Empty, form.Summary);
        Assert.Equal(string.Empty, form.EventDate);
        Assert.Equal(default, form.DatePrecision);
        Assert.Equal(default, form.Category);
        Assert.Equal(0, form.Importance);
        Assert.Null(form.SourceUrl);
        Assert.False(form.IsPublished);
    }

    [Fact]
    public void ToDraft_trims_fields_and_parses_event_date_as_utc_midnight()
    {
        var form = new AdminTimelineForm
        {
            Title = "  Live Aid  ",
            Summary = "  Queen at Wembley.  ",
            EventDate = "1985-07-13",
            DatePrecision = QueenHistoryDatePrecision.ExactDate,
            Category = QueenHistoryEventCategory.Concert,
            Importance = 100,
            SourceUrl = "  https://example.com/live-aid  ",
            IsPublished = true,
        };

        var draft = form.ToDraft();

        Assert.Equal("Live Aid", draft.Title);
        Assert.Equal("Queen at Wembley.", draft.Summary);
        Assert.Equal(new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc), draft.EventDate);
        Assert.Equal(QueenHistoryDatePrecision.ExactDate, draft.DatePrecision);
        Assert.Equal(QueenHistoryEventCategory.Concert, draft.Category);
        Assert.Equal(100, draft.Importance);
        Assert.Equal("https://example.com/live-aid", draft.SourceUrl);
        Assert.True(draft.IsPublished);
    }

    [Fact]
    public void ParseRowVersion_accepts_base64_and_rejects_invalid_values()
    {
        var token = Convert.ToBase64String([1, 2, 3, 4]);
        Assert.Equal([1, 2, 3, 4], new AdminTimelineForm { RowVersion = token }.ParseRowVersion());
        Assert.Null(new AdminTimelineForm().ParseRowVersion());
        Assert.Null(new AdminTimelineForm { RowVersion = " " }.ParseRowVersion());
        Assert.Null(new AdminTimelineForm { RowVersion = "not-base64" }.ParseRowVersion());
    }

    [Fact]
    public void ToDraft_treats_null_fields_as_empty()
    {
        var form = new AdminTimelineForm
        {
            Title = null!,
            Summary = null!,
            EventDate = null!,
            SourceUrl = "   ",
            IsPublished = false,
        };

        var draft = form.ToDraft();

        Assert.Equal(string.Empty, draft.Title);
        Assert.Equal(string.Empty, draft.Summary);
        Assert.Equal(default, draft.EventDate);
        Assert.Null(draft.SourceUrl);
        Assert.False(draft.IsPublished);
    }

    [Fact]
    public void NewModel_BuildForm_creates_add_timeline_view_model()
    {
        var draft = new AdminQueenHistoryDraft(
            "Title",
            "Summary",
            new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc),
            QueenHistoryDatePrecision.ExactDate,
            QueenHistoryEventCategory.Other,
            50,
            null,
            true);

        var form = NewModel.BuildForm(draft, ["Title is required."]);

        Assert.Equal("Add timeline event", form.Title);
        Assert.Equal("/admin/timeline", form.Action);
        Assert.Same(draft, form.Draft);
        Assert.Equal("Title is required.", Assert.Single(form.Errors!));
        Assert.Null(form.Event);
    }

    [Fact]
    public void NewModel_defaults_empty_title_today_utc_exact_other_importance_50_published()
    {
        var dayBefore = DateTime.UtcNow.Date;
        var draft = new NewModel().Form.Draft;
        var dayAfter = DateTime.UtcNow.Date;

        Assert.Equal(string.Empty, draft.Title);
        Assert.Equal(string.Empty, draft.Summary);
        Assert.Equal(DateTimeKind.Utc, draft.EventDate.Kind);
        Assert.Equal(draft.EventDate.Date, draft.EventDate);
        Assert.InRange(draft.EventDate, dayBefore, dayAfter);
        Assert.Equal(QueenHistoryDatePrecision.ExactDate, draft.DatePrecision);
        Assert.Equal(QueenHistoryEventCategory.Other, draft.Category);
        Assert.Equal(50, draft.Importance);
        Assert.Null(draft.SourceUrl);
        Assert.True(draft.IsPublished);
    }

    [Fact]
    public void EditModel_BuildForm_creates_edit_timeline_view_model()
    {
        var historyEvent = new QueenHistoryEvent(
            7,
            "Live Aid",
            "Queen at Wembley.",
            new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc),
            QueenHistoryDatePrecision.ExactDate,
            QueenHistoryEventCategory.Concert,
            100,
            QueenHistoryEventSourceType.Curated,
            "curated:7",
            null,
            true);
        var draft = new AdminQueenHistoryDraft(
            historyEvent.Title,
            historyEvent.Summary,
            historyEvent.EventDate,
            historyEvent.DatePrecision,
            historyEvent.Category,
            historyEvent.Importance,
            historyEvent.SourceUrl,
            historyEvent.IsPublished);

        var form = EditModel.BuildForm(historyEvent, draft, null);

        Assert.Equal("Edit timeline event", form.Title);
        Assert.Equal("/admin/timeline/7", form.Action);
        Assert.Same(draft, form.Draft);
        Assert.Null(form.Errors);
        Assert.Equal(7, form.Event!.Id);
    }
}
