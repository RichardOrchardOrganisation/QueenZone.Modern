using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class MemberAvatarEndpointsTests :
    IClassFixture<InspectableBlobWebApplicationFactory>,
    IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly InspectableBlobWebApplicationFactory resettableFactory;

    public MemberAvatarEndpointsTests(InspectableBlobWebApplicationFactory factory)
    {
        this.factory = factory;
        resettableFactory = factory;
    }

    public Task InitializeAsync() => resettableFactory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAvatar_ReturnsNotFound_WhenMemberHasNoAvatar()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"/account/avatar/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAvatar_StreamsStoredWebp_WhenAvatarExists()
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        Guid memberId;
        using (var scope = scopeFactory.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
            var registered = await members.RegisterAsync(
                "avatar-serve@example.com",
                "S3curePass!",
                "Serve Fan");
            memberId = registered.Account!.Id;
            await using var png = await CreatePngAsync();
            var upload = await members.UpdateAvatarAsync(memberId, png, "face.png");
            Assert.True(upload.Succeeded, upload.Error);
        }

        var client = factory.CreateClient();
        var response = await client.GetAsync($"/account/avatar/{memberId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        var thumb = await client.GetAsync($"/account/avatar/{memberId:D}?size=thumb");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
    }

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(48, 48, new Rgba32(200, 40, 40));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
