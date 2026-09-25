using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiPhotosTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string PhotosRoot = $"{ContentApiEndpoints.RootPath}/photos";
    private const string BrianMaySlug = "brian-may";
    private const string PhotoCdnOrigin = "https://cdn.queenzone.org/";

    private readonly QueenZoneWebApplicationFactory factory;

    public ContentApiPhotosTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Photo_categories_require_no_auth_and_return_cdn_covers()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoCategoryListItemDto>>();
        Assert.NotNull(payload);
        Assert.True(payload!.Items.Count >= 3);
        Assert.Contains(payload.Items, item => item.Slug == BrianMaySlug);
        Assert.All(payload.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Slug));
            Assert.True(item.ImageCount > 0);
            Assert.Equal($"/photography/{item.Slug}", item.DetailPath);
            Assert.StartsWith(PhotoCdnOrigin, item.CoverThumbnailUrl);
        });
    }

    [Fact]
    public async Task Photo_categories_clamp_invalid_paging_query_values()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories?page=0&pageSize=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoCategoryListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(ApiPagination.MaxPageSize, payload.PageSize);
    }

    [Fact]
    public async Task Photo_category_detail_returns_the_gallery()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var category = await response.Content.ReadFromJsonAsync<PhotoCategoryListItemDto>();
        Assert.NotNull(category);
        Assert.Equal(9, category!.CatId);
        Assert.Equal("Brian May", category.Name);
        Assert.Equal(BrianMaySlug, category.Slug);
        Assert.Equal(3, category.ImageCount);
        Assert.Equal($"/photography/{BrianMaySlug}", category.DetailPath);
        Assert.StartsWith(PhotoCdnOrigin, category.CoverThumbnailUrl);
    }

    [Fact]
    public async Task Photo_category_detail_returns_problem_details_for_unknown_slug()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
        Assert.Contains("does-not-exist", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_items_default_and_clamp_to_website_page_size()
    {
        using var client = factory.CreateAnonymousClient();

        using var omitted = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}/items");
        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
        var omittedPage = await omitted.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoListItemDto>>();
        Assert.NotNull(omittedPage);
        Assert.Equal(1, omittedPage!.Page);
        Assert.Equal(PhotoRoutes.CategoryPageSize, omittedPage.PageSize);
        Assert.Equal(3, omittedPage.Items.Count);
        Assert.Equal(3, omittedPage.TotalCount);

        using var clamped = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items?page=0&pageSize=1000");
        Assert.Equal(HttpStatusCode.OK, clamped.StatusCode);
        var clampedPage = await clamped.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoListItemDto>>();
        Assert.NotNull(clampedPage);
        Assert.Equal(1, clampedPage!.Page);
        Assert.Equal(PhotoRoutes.CategoryPageSize, clampedPage.PageSize);
        Assert.Equal(3, clampedPage.Items.Count);
    }

    [Fact]
    public async Task Photo_items_return_thumbnails_only_on_cdn()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items?page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("page").GetInt32());
        Assert.Equal(1, payload.GetProperty("pageSize").GetInt32());
        var item = payload.GetProperty("items")[0];
        Assert.Equal(101, item.GetProperty("picId").GetInt32());
        Assert.Equal("Brian in action with his guitar", item.GetProperty("title").GetString());
        Assert.Equal($"{PhotoCdnOrigin}brian-may/img-101-t.jpg", item.GetProperty("thumbnailUrl").GetString());
        Assert.Equal($"/photography/{BrianMaySlug}/101", item.GetProperty("detailPath").GetString());
        Assert.Equal($"/photography/{BrianMaySlug}", item.GetProperty("categoryPath").GetString());
        Assert.False(item.TryGetProperty("imageUrl", out _));
    }

    [Fact]
    public async Task Photo_items_honour_size_filter_and_return_empty_past_last_page()
    {
        using var client = factory.CreateAnonymousClient();

        using var filtered = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items?size=desktop");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var filteredPage = await filtered.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoListItemDto>>();
        Assert.NotNull(filteredPage);
        Assert.Equal(1, filteredPage!.TotalCount);
        Assert.Equal(101, filteredPage.Items[0].PicId);
        Assert.Equal($"/photography/{BrianMaySlug}/101?size=desktop", filteredPage.Items[0].DetailPath);

        using var pastLast = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items?page=999&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pastLast.StatusCode);
        var empty = await pastLast.Content.ReadFromJsonAsync<ApiPagedResponse<PhotoListItemDto>>();
        Assert.NotNull(empty);
        Assert.Empty(empty!.Items);
    }

    [Fact]
    public async Task Photo_items_return_problem_details_for_unknown_category()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/missing/items");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Photo_detail_returns_cdn_original_and_neighbors()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}/items/102");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photo = await response.Content.ReadFromJsonAsync<PhotoDetailDto>();
        Assert.NotNull(photo);
        Assert.Equal(102, photo!.PicId);
        Assert.Equal("Soundcheck, Wembley", photo.Title);
        Assert.Equal($"{PhotoCdnOrigin}brian-may/img-102.jpg", photo.ImageUrl);
        Assert.Equal($"{PhotoCdnOrigin}brian-may/img-102-t.jpg", photo.ThumbnailUrl);
        Assert.Equal(1, photo.Index);
        Assert.Equal(3, photo.Count);
        Assert.Equal(101, photo.Previous!.PicId);
        Assert.Equal($"/photography/{BrianMaySlug}/101", photo.Previous.DetailPath);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-101.jpg"), photo.Previous.ImageUrl);
        Assert.Equal(1920, photo.Previous.PictureWidth);
        Assert.Equal(1080, photo.Previous.PictureHeight);
        Assert.Equal(103, photo.Next!.PicId);
        Assert.Equal($"/photography/{BrianMaySlug}/103", photo.Next.DetailPath);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-103.jpg"), photo.Next.ImageUrl);
        Assert.Equal(0, photo.Next.PictureWidth);
        Assert.Equal(0, photo.Next.PictureHeight);
        Assert.Equal("RedSpecial", photo.SubmittedByDisplayName);
        Assert.Equal("1600 x 1200", photo.PictureDimensionsLabel);
        Assert.Equal($"/photography/{BrianMaySlug}", photo.CategoryPath);
    }

    [Fact]
    public async Task Photo_detail_ends_have_null_neighbors()
    {
        using var client = factory.CreateAnonymousClient();

        using var first = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}/items/101");
        var firstPhoto = await first.Content.ReadFromJsonAsync<PhotoDetailDto>();
        Assert.NotNull(firstPhoto);
        Assert.Null(firstPhoto!.Previous);
        Assert.Equal(102, firstPhoto.Next!.PicId);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-102.jpg"), firstPhoto.Next.ImageUrl);

        using var last = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}/items/103");
        var lastPhoto = await last.Content.ReadFromJsonAsync<PhotoDetailDto>();
        Assert.NotNull(lastPhoto);
        Assert.Equal(102, lastPhoto!.Previous!.PicId);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-102.jpg"), lastPhoto.Previous.ImageUrl);
        Assert.Null(lastPhoto.Next);
        Assert.Null(lastPhoto.PictureDimensionsLabel);
    }

    [Fact]
    public async Task Photo_detail_falls_back_when_size_filter_excludes_the_photo()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items/103?size=desktop");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var photo = await response.Content.ReadFromJsonAsync<PhotoDetailDto>();
        Assert.NotNull(photo);
        Assert.Equal(103, photo!.PicId);
        Assert.Equal(2, photo.Index);
        Assert.Equal(3, photo.Count);
        Assert.Equal($"/photography/{BrianMaySlug}/103", photo.DetailPath);
    }

    [Fact]
    public async Task Photo_detail_returns_problem_details_for_missing_photo()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/{BrianMaySlug}/items/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("424242", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_detail_returns_problem_details_for_unknown_category()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{PhotosRoot}/categories/does-not-exist/items/101");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("does-not-exist", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_detail_returns_problem_details_when_filtered_photo_is_missing()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(
            $"{PhotosRoot}/categories/{BrianMaySlug}/items/424242?size=desktop");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void ToPhotoListItem_uses_thumbnail_cdn_url_and_website_detail_path()
    {
        var item = new PhotoItem(
            101,
            9,
            "Brian May",
            BrianMaySlug,
            "Brian in action with his guitar",
            "https://cdn.queenzone.org/brian-may/img-101.jpg",
            "https://cdn.queenzone.org/brian-may/img-101-t.jpg",
            150,
            150,
            1920,
            1080,
            1986,
            new DateTime(1986, 7, 12),
            "QueenFan86");

        var dto = ContentApiMapper.ToPhotoListItem(item);

        Assert.Equal("https://cdn.queenzone.org/brian-may/img-101-t.jpg", dto.ThumbnailUrl);
        Assert.Equal($"/photography/{BrianMaySlug}/101", dto.DetailPath);
        Assert.Equal($"/photography/{BrianMaySlug}", dto.CategoryPath);
        Assert.Equal("1920 x 1080", dto.PictureDimensionsLabel);
    }

    [Fact]
    public void ToPhotoDetail_maps_neighbor_cdn_urls_from_legacy_file_paths()
    {
        var category = new PhotoCategory(9, "Brian May", BrianMaySlug, 3, "https://cdn.queenzone.org/brian-may/img-101-t.jpg");
        var photo = new PhotoItem(
            102,
            9,
            "Brian May",
            BrianMaySlug,
            "Soundcheck, Wembley",
            "https://cdn.queenzone.org/brian-may/img-102.jpg",
            "https://cdn.queenzone.org/brian-may/img-102-t.jpg",
            150,
            150,
            1600,
            1200,
            1986,
            new DateTime(1986, 7, 11),
            "RedSpecial");
        var navigation = new PhotoDetailNavigation(
            photo,
            1,
            3,
            101,
            103,
            PreviousMedia: new PhotoNeighborMedia("/Brian_May/img-101.jpg", 1920, 1080),
            NextMedia: new PhotoNeighborMedia("/Brian_May/img-103.jpg", 0, 0));

        var dto = ContentApiMapper.ToPhotoDetail(category, navigation);

        Assert.Equal("https://cdn.queenzone.org/brian-may/img-102.jpg", dto.ImageUrl);
        Assert.Equal($"/photography/{BrianMaySlug}", dto.CategoryPath);
        Assert.Equal(101, dto.Previous!.PicId);
        Assert.Equal($"/photography/{BrianMaySlug}/101", dto.Previous.DetailPath);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-101.jpg"), dto.Previous.ImageUrl);
        Assert.Equal(1920, dto.Previous.PictureWidth);
        Assert.Equal(1080, dto.Previous.PictureHeight);
        Assert.Equal(103, dto.Next!.PicId);
        Assert.Equal(PhotoImageUrl.Build("/Brian_May/img-103.jpg"), dto.Next.ImageUrl);
        Assert.Equal(0, dto.Next.PictureWidth);
        Assert.Equal(0, dto.Next.PictureHeight);
    }
}
