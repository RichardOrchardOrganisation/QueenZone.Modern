using Microsoft.AspNetCore.Http;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web.Pages.Admin.News;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class NewsArticleGalleryPickerTests
{
    [Fact]
    public void BuildFilter_reuses_admin_photo_search_and_category()
    {
        var filter = NewsArticleGalleryPicker.BuildFilter(9, "  Wembley  ");

        Assert.Equal(9, filter.CatId);
        Assert.Equal("Wembley", filter.Search);
        Assert.Null(filter.IsVisible);
        Assert.Null(filter.Year);
        Assert.True(filter.UsableForCardCrop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Card_crop_filter_fills_pages_and_counts_only_usable_photos_in_category(bool usePageModel)
    {
        var date = new DateTime(2026, 1, 1);
        var usable = Enumerable.Range(1, 10)
            .Select(id => new PhotoItemSeed(id, "Usable", "/Brian_May/photo.jpg", "/Brian_May/thumb.jpg", date, 400, 267))
            .ToArray();
        var store = new SharedPhotoStore(
        [
            new PhotoCategorySeed(9, "Brian May",
            [
                .. usable,
                new PhotoItemSeed(11, "Unknown", "/Brian_May/unknown.jpg", "", date.AddDays(1), 0, 0),
                new PhotoItemSeed(12, "Small", "/Brian_May/small.jpg", "", date.AddDays(1), 384, 288),
            ]),
            new PhotoCategorySeed(12, "Queen",
            [
                new PhotoItemSeed(13, "Other category", "/Queen/photo.jpg", "", date.AddDays(2), 800, 600),
            ]),
        ]);
        var repository = new InMemoryAdminPhotoRepository(store);
        var model = new GalleryPickerModel(repository) { CatId = 9 };

        async Task<AdminPhotoPage> GetPageAsync(int pageNumber)
        {
            if (!usePageModel)
            {
                return await repository.GetPageAsync(
                    new AdminPhotoListFilter(CatId: 9, UsableForCardCrop: true), pageNumber, NewsArticleGalleryPicker.PageSize);
            }

            model.PageNumber = pageNumber;
            await model.OnGetAsync(CancellationToken.None);
            return Assert.IsType<AdminPhotoPage>(model.Photos);
        }

        var first = await GetPageAsync(1);
        Assert.Equal(10, first.TotalCount);
        Assert.Equal(8, first.PageSize);
        Assert.Equal(first.PageSize, first.Items.Count);
        Assert.Equal(Enumerable.Range(3, 8).Reverse(), first.Items.Select(photo => photo.PicId));
        Assert.All(first.Items, photo => Assert.Equal(9, photo.CatId));

        var second = await GetPageAsync(2);
        Assert.Equal(10, second.TotalCount);
        Assert.Equal([2, 1], second.Items.Select(photo => photo.PicId));
        Assert.All(second.Items, photo => Assert.Equal(9, photo.CatId));
    }

    [Fact]
    public void FileName_uses_legacy_path_basename()
    {
        var photo = new AdminPhotoItem(
            101,
            9,
            "Brian May",
            "brian-may",
            "Brian in action with his guitar",
            "/Brian_May/img-101.jpg",
            "/Brian_May/img-101-t.jpg",
            "https://cdn.queenzone.org/brian-may/img-101.jpg",
            "https://cdn.queenzone.org/brian-may/img-101-t.jpg",
            150,
            150,
            1920,
            1080,
            1986,
            new DateTime(1986, 7, 12),
            null,
            true);

        Assert.Equal("img-101.jpg", NewsArticleGalleryPicker.GetFileName(photo));
    }

    [Fact]
    public void BuildPath_includes_filter_and_page_query()
    {
        Assert.Equal("/admin/news/gallery-picker", NewsArticleGalleryPicker.BuildPath(null, null, 1));
        Assert.Equal(
            "/admin/news/gallery-picker?catId=9&q=Wembley&pageNumber=2",
            NewsArticleGalleryPicker.BuildPath(9, "Wembley", 2));
        Assert.Equal("/admin/news/gallery-original/101", NewsArticleGalleryPicker.BuildOriginalPath(101));
    }

    [Fact]
    public void TryResolveBlobLocation_uses_legacy_pic_path()
    {
        Assert.True(NewsArticleGalleryPicker.TryResolveBlobLocation("/Brian_May/img-101.jpg", out var container, out var blobName));
        Assert.Equal("brian-may", container);
        Assert.Equal("img-101.jpg", blobName);
    }

    [Fact]
    public async Task OpenOriginalAsync_reads_pic_bytes_without_writing()
    {
        var photos = new InMemoryAdminPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var gallery = new NullGalleryPhotoBlobService();
        using var image = new Image<Rgba32>(600, 400);
        await using var jpeg = new MemoryStream();
        await image.SaveAsync(jpeg, new JpegEncoder());
        jpeg.Position = 0;
        await gallery.UploadAsync("brian-may", "img-101.jpg", jpeg, "image/jpeg");

        var opened = await NewsArticleGalleryPicker.OpenOriginalAsync(photos, gallery, 101);
        Assert.NotNull(opened);
        Assert.Equal("image/jpeg", opened.ContentType);
        Assert.Equal("img-101.jpg", opened.FileName);
        await using (opened.Stream)
        {
            Assert.True(opened.Stream.Length > 0);
        }

        Assert.Null(await NewsArticleGalleryPicker.OpenOriginalAsync(photos, gallery, 99999));
    }

    [Fact]
    public async Task ResolvePreviewUrl_uses_gallery_cdn_image()
    {
        var photos = new InMemoryAdminPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));
        var draft = NewsArticleImage.WithGalleryPick(
            new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            101);

        var preview = await NewsArticleGalleryPicker.ResolvePreviewUrlAsync(photos, draft);

        Assert.Equal("https://cdn.queenzone.org/brian-may/img-101.jpg", preview);
    }

    [Fact]
    public async Task ValidatePic_rejects_missing_gallery_row_without_upload()
    {
        var photos = new InMemoryAdminPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));

        var missing = await NewsArticleGalleryPicker.ValidatePicAsync(photos, uploadedFile: null, 99999);
        Assert.Equal("That gallery photo was not found.", missing);

        var found = await NewsArticleGalleryPicker.ValidatePicAsync(photos, uploadedFile: null, 101);
        Assert.Null(found);

        await using var stream = new MemoryStream("x"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "articleImage", "hero.png");
        var skipped = await NewsArticleGalleryPicker.ValidatePicAsync(photos, file, 99999);
        Assert.Null(skipped);
    }

    [Fact]
    public void ToDraft_normalizes_gallery_pick_over_previous_blob_key()
    {
        var form = new AdminNewsForm
        {
            Title = "Gallery pick",
            Excerpt = "Excerpt",
            Body = "Body",
            PublishedAt = "2026-06-14",
            ImageBlobKey = "editors/me/old.webp",
            ImageGalleryPicId = 101
        };

        var draft = form.ToDraft();

        Assert.Equal("gallery:101", draft.ImageBlobKey);
        Assert.Equal(101, draft.ImageGalleryPicId);
    }
}
