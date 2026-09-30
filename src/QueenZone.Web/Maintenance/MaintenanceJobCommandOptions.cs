namespace QueenZone.Web;

/// <summary>
/// Parses <c>run-jobs &lt;selector&gt;...</c> for <c>QueenZone.Maintenance.Worker</c>. A selector is
/// <c>six-hourly</c>, <c>daily</c>, <c>all</c>, or a single job name from <see cref="MaintenanceJobs"/>.
/// </summary>
public sealed record MaintenanceJobCommandOptions(IReadOnlyList<string> Jobs)
{
    public const string CommandName = "run-jobs";

    public static MaintenanceJobCommandOptions? Parse(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !string.Equals(args[0], CommandName, StringComparison.Ordinal))
        {
            return null;
        }

        var jobs = new List<string>();
        foreach (var selector in args.Skip(1))
        {
            var resolved = MaintenanceJobs.Resolve(selector);
            if (resolved is null)
            {
                return null;
            }

            foreach (var job in resolved)
            {
                if (!jobs.Contains(job, StringComparer.Ordinal))
                {
                    jobs.Add(job);
                }
            }
        }

        return new MaintenanceJobCommandOptions(jobs);
    }
}
