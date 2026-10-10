using System.Collections.Concurrent;
using Microsoft.AspNetCore.OutputCaching;

namespace QueenZone.Web.Tests;

public sealed class OutputCacheExpirationObserver : IOutputCachePolicy
{
    public ConcurrentDictionary<string, TimeSpan> Durations { get; } = new();

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        if (context.ResponseExpirationTimeSpan is { } duration)
        {
            Durations[context.HttpContext.Request.Path.Value!] = duration;
        }
        return ValueTask.CompletedTask;
    }
}
