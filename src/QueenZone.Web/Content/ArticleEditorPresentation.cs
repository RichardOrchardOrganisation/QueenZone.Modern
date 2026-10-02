namespace QueenZone.Web;

public sealed record ArticleEditorPresentation(string Title, string FormAction)
{
    public static ArticleEditorPresentation For(Guid? id) => id is Guid existingId
        ? new("Edit article", $"/admin/articles/editor/{existingId}")
        : new("Create article", "/admin/articles/editor");
}
