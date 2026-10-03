namespace QueenZone.Web.Tests;

public sealed class ArticleEditorPresentationTests
{
    [Fact]
    public void New_article_has_create_title_and_endpoint()
    {
        var view = ArticleEditorPresentation.For(null);
        Assert.Equal("Create article", view.Title);
        Assert.Equal("/admin/articles/editor", view.FormAction);
    }

    [Fact]
    public void Existing_article_keeps_identity_in_edit_endpoint()
    {
        var id = Guid.Parse("c04a265a-f33e-4a18-883c-55ef50b0d211");
        var view = ArticleEditorPresentation.For(id);
        Assert.Equal("Edit article", view.Title);
        Assert.Equal($"/admin/articles/editor/{id}", view.FormAction);
    }
}
