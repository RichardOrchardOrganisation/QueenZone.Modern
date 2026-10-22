namespace QueenZone.Web.Sitemap;

public interface ICoreSitemapService
{
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
