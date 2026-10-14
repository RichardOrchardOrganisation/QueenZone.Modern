using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.News;

public sealed class GalleryPickerModel(IAdminPhotoRepository adminPhotoRepository) : AdminNewsPageModel
{
    [BindProperty(SupportsGet = true)]
    public int? CatId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public AdminPhotoPage? Photos { get; private set; }

    public IReadOnlyList<AdminPhotoCategory> Categories { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Categories = await adminPhotoRepository.GetCategoriesAsync(cancellationToken);
        var page = Math.Max(PageNumber, 1);
        Photos = await adminPhotoRepository.GetPageAsync(
            NewsArticleGalleryPicker.BuildFilter(CatId, Q),
            page,
            NewsArticleGalleryPicker.PageSize,
            cancellationToken);
        ViewData["Title"] = "Choose a gallery photo";
    }
}
