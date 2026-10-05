using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsDiscoveryService(
    INewsDiscoveryRepository repository,
    NewsSourceFetcherRegistry fetcherRegistry,
    ILogger<NewsDiscoveryService> logger)
{
    public async Task<NewsDiscoveryRunResult> RunFetchAsync(
        NewsDiscoveryRunOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.SeedSources)
        {
            var seeded = await NewsDiscoverySourceSeeder.SeedAsync(repository, cancellationToken);
            logger.LogInformation("Seeded or updated {SeededSourceCount} discovery sources.", seeded);
        }

        var runAt = options.RunAtUtc ?? DateTime.UtcNow;
        var sources = await repository.GetSourcesAsync(enabledOnly: true, cancellationToken);
        var errors = new List<string>();
        var sourcesChecked = 0;
        var sourcesSkipped = 0;
        var itemsFetched = 0;
        var candidatesCreated = 0;
        var duplicatesSkipped = 0;
        var keywordFiltered = 0;
        var failures = 0;

        foreach (var source in sources)
        {
            if (!options.Force && !ShouldFetch(source, runAt))
            {
                sourcesSkipped++;
                logger.LogInformation(
                    "Skipping source {SourceKey}; last fetched at {LastFetchedAt}.",
                    source.Key,
                    source.LastFetchedAt);
                continue;
            }

            sourcesChecked++;
            try
            {
                var fetcher = fetcherRegistry.GetFetcher(source.SourceType);
                var items = await fetcher.FetchAsync(source, cancellationToken);
                itemsFetched += items.Count;

                var (created, duplicates, filtered) = await IngestItemsAsync(
                    source, items, runAt, options.DryRun, cancellationToken);

                candidatesCreated += created;
                duplicatesSkipped += duplicates;
                keywordFiltered += filtered;

                if (!options.DryRun)
                {
                    await repository.MarkSourceFetchedAsync(source.Id, runAt, cancellationToken);
                }

                logger.LogInformation(
                    "Source {SourceKey}: fetched {ItemCount}, created {CreatedCount}, skipped duplicates {DuplicateCount}, keyword filtered {FilteredCount}.",
                    source.Key,
                    items.Count,
                    created,
                    duplicates,
                    filtered);
            }
            catch (Exception ex)
            {
                failures++;
                var message = $"{source.Key}: {ex.Message}";
                errors.Add(message);
                logger.LogError(ex, "Discovery fetch failed for source {SourceKey}.", source.Key);
            }
        }

        return new NewsDiscoveryRunResult(
            sourcesChecked,
            sourcesSkipped,
            itemsFetched,
            candidatesCreated,
            duplicatesSkipped,
            keywordFiltered,
            failures,
            errors);
    }

    private async Task<(int Created, int Duplicates, int Filtered)> IngestItemsAsync(
        NewsDiscoverySource source,
        IReadOnlyList<FetchedNewsItem> items,
        DateTime runAt,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var duplicates = 0;
        var filtered = 0;
        foreach (var item in items)
        {
            if (!NewsDiscoveryUrlFilter.IsLikelyArticleUrl(item.SourceUrl))
            {
                continue;
            }

            if (!NewsDiscoveryKeywordFilter.Matches(source, item))
            {
                filtered++;
                continue;
            }

            var ingest = await TryIngestItemAsync(source, item, runAt, dryRun, cancellationToken);
            created += ingest.Created ? 1 : 0;
            duplicates += ingest.DuplicateSkipped ? 1 : 0;
        }
        return (created, duplicates, filtered);
    }

    private async Task<(bool Created, bool DuplicateSkipped)> TryIngestItemAsync(
        NewsDiscoverySource source,
        FetchedNewsItem item,
        DateTime discoveredAt,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var canonicalUrl = NewsCandidateDedupe.NormalizeCanonicalUrl(item.SourceUrl);
        var canonicalUrlHash = NewsCandidateDedupe.ComputeUrlHash(canonicalUrl);
        var contentHash = NewsCandidateDedupe.ComputeContentHash(item.Title, item.Excerpt);

        if (await repository.GetCandidateByCanonicalUrlHashAsync(canonicalUrlHash, cancellationToken) is not null
            || await repository.GetCandidateByContentHashAsync(contentHash, cancellationToken) is not null)
        {
            return (false, true);
        }

        if (dryRun)
        {
            return (true, false);
        }

        await repository.CreateCandidateAsync(
            new NewsCandidateCreateRequest(
                source.Id,
                item.SourceUrl,
                item.Title,
                item.PublishedAt,
                item.Excerpt,
                discoveredAt),
            cancellationToken);

        return (true, false);
    }

    private static bool ShouldFetch(NewsDiscoverySource source, DateTime runAt)
    {
        if (source.LastFetchedAt is null)
        {
            return true;
        }

        return source.LastFetchedAt.Value.AddMinutes(source.PollIntervalMinutes) <= runAt;
    }
}
