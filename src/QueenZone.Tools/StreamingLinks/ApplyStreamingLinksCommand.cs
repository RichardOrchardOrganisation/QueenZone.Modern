using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>What <c>apply-streaming-links</c> does (or would do) with one approved CSV row.</summary>
internal enum StreamingLinkApplyAction
{
    Insert,
    Update,
    Unchanged,
    ManualProtected,
    Invalid,
}

/// <summary>
/// One approved row and its planned action. <see cref="Write"/> is set for
/// <see cref="StreamingLinkApplyAction.Insert"/> and <see cref="StreamingLinkApplyAction.Update"/>.
/// </summary>
internal sealed record StreamingLinkApplyPlan(
    int RowNumber,
    StreamingLinkApplyAction Action,
    int? AlbumId,
    int? AlbumSongId,
    StreamingProvider? Provider,
    string Detail,
    StreamingLinkWrite? Write = null)
{
    public string Target => AlbumSongId is { } songId
        ? $"track {songId} (album {AlbumId})"
        : $"album {AlbumId?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
}

/// <summary>A CSV data row: its 1-based line number and fields keyed by header name.</summary>
internal sealed record StreamingLinkCsvRow(int RowNumber, IReadOnlyDictionary<string, string> Fields)
{
    public string this[string column] => Fields.TryGetValue(column, out var value) ? value.Trim() : string.Empty;
}

/// <summary>
/// <c>apply-streaming-links</c>: imports the rows of a reviewed <c>suggest-streaming-links</c> CSV
/// that have <c>approved=yes</c>. Dry run by default. Every URL is re-validated with
/// <see cref="StreamingLinkUrl"/>, links are written as <see cref="StreamingLinkSource.Imported"/>,
/// manual links are kept unless <c>--overwrite-manual</c> is passed, and re-running the same file
/// changes nothing. See <c>docs/architecture/streaming-links-backfill.md</c>.
/// </summary>
internal static class ApplyStreamingLinksCommand
{
    public const string UpdatedBy = "apply-streaming-links";

    private const string AlbumIdColumn = "albumId";
    private const string AlbumSongIdColumn = "albumSongId";
    private const string ProviderColumn = "provider";
    private const string CandidateUrlColumn = "candidateUrl";
    private const string ApprovedColumn = "approved";

    private static readonly string[] RequiredColumns =
        [AlbumIdColumn, AlbumSongIdColumn, ProviderColumn, CandidateUrlColumn, ApprovedColumn];

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ApplyStreamingLinksOptions.Parse(args);
        if (!options.IsValid)
        {
            WriteUsage(options.ErrorMessage);
            return 2;
        }

        if (!File.Exists(options.FilePath))
        {
            await Console.Error.WriteLineAsync($"CSV file was not found: {options.FilePath}");
            return 2;
        }

        var dbOptions = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(options.ConnectionString).Options;
        await using var dbContext = new QueenZoneDbContext(dbOptions);
        using var csv = new StreamReader(options.FilePath, detectEncodingFromByteOrderMarks: true);
        return await RunAsync(options, new EfAdminDiscographyRepository(dbContext), csv, Console.Out);
    }

    internal static async Task<int> RunAsync(
        ApplyStreamingLinksOptions options,
        IAdminDiscographyRepository repository,
        TextReader csv,
        TextWriter output)
    {
        IReadOnlyList<StreamingLinkCsvRow> rows;
        try
        {
            rows = ReadCsv(csv);
        }
        catch (Exception ex) when (ex is InvalidOperationException or MalformedLineException)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 2;
        }

        var approved = rows.Where(IsApproved).ToList();
        var plans = await PlanAsync(approved, repository, options.OverwriteManual, options.CancellationToken);
        foreach (var plan in plans)
        {
            await output.WriteLineAsync(Describe(plan));
        }

        var failed = options.Apply ? await ExecuteAsync(plans, repository, output, options.CancellationToken) : 0;
        await WriteSummaryAsync(output, options, rows.Count - approved.Count, plans, failed);
        return plans.Any(plan => plan.Action == StreamingLinkApplyAction.Invalid) || failed > 0 ? 1 : 0;
    }

    /// <summary>Reads the CSV by header name, so a spreadsheet may reorder or add columns.</summary>
    internal static IReadOnlyList<StreamingLinkCsvRow> ReadCsv(TextReader reader)
    {
        using var parser = new TextFieldParser(reader);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        var headers = parser.ReadFields()?.Select(header => header.Trim().TrimStart('﻿')).ToArray()
            ?? throw new InvalidOperationException("CSV file is empty.");
        var missing = RequiredColumns.Where(column => !headers.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"CSV is missing column(s): {string.Join(", ", missing)}. Use the file written by suggest-streaming-links.");
        }

        var rows = new List<StreamingLinkCsvRow>();
        while (!parser.EndOfData)
        {
            var rowNumber = (int)parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length && index < fields.Length; index++)
            {
                values.TryAdd(headers[index], fields[index]);
            }

            rows.Add(new StreamingLinkCsvRow(rowNumber, values));
        }

        return rows;
    }

    internal static bool IsApproved(StreamingLinkCsvRow row) =>
        string.Equals(row[ApprovedColumn], "yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>Decides insert, update or skip for each approved row. It does not write.</summary>
    internal static async Task<IReadOnlyList<StreamingLinkApplyPlan>> PlanAsync(
        IReadOnlyList<StreamingLinkCsvRow> approvedRows,
        IAdminDiscographyRepository repository,
        bool overwriteManual,
        CancellationToken cancellationToken)
    {
        var albums = new Dictionary<int, AdminAlbum?>();
        var seen = new Dictionary<(int AlbumId, int? AlbumSongId, StreamingProvider Provider), int>();
        var plans = new List<StreamingLinkApplyPlan>();
        foreach (var row in approvedRows)
        {
            var parsed = ParseRow(row);
            if (parsed.Plan is not null)
            {
                plans.Add(parsed.Plan);
                continue;
            }

            var (albumId, songId, provider, link) = parsed.Row!.Value;
            if (seen.TryGetValue((albumId, songId, provider), out var firstRow))
            {
                plans.Add(Invalid(row.RowNumber, albumId, songId, provider, $"duplicate of row {firstRow}"));
                continue;
            }

            seen[(albumId, songId, provider)] = row.RowNumber;
            if (!albums.TryGetValue(albumId, out var album))
            {
                album = await repository.GetAlbumAsync(albumId, cancellationToken);
                albums[albumId] = album;
            }

            plans.Add(Decide(row.RowNumber, album, albumId, songId, link, overwriteManual));
        }

        return plans;
    }

    private static StreamingLinkApplyPlan Decide(
        int rowNumber,
        AdminAlbum? album,
        int albumId,
        int? songId,
        ParsedStreamingLink link,
        bool overwriteManual)
    {
        var provider = link.Provider;
        if (album is null)
        {
            return Invalid(rowNumber, albumId, songId, provider, $"album {albumId} was not found");
        }

        IReadOnlyList<AdminStreamingLink> links = album.StreamingLinks;
        if (songId is not null)
        {
            var song = album.Songs.FirstOrDefault(candidate => candidate.SongId == songId);
            if (song is null)
            {
                return Invalid(rowNumber, albumId, songId, provider, $"track {songId} is not on album {albumId}");
            }

            links = song.StreamingLinks;
        }

        var existing = links.FirstOrDefault(candidate => candidate.Provider == provider);
        var write = new StreamingLinkWrite(link, StreamingLinkSource.Imported, UpdatedBy);
        if (existing is null)
        {
            return new StreamingLinkApplyPlan(rowNumber, StreamingLinkApplyAction.Insert, albumId, songId, provider, link.Url, write);
        }

        if (string.Equals(existing.Url, link.Url, StringComparison.Ordinal))
        {
            return new StreamingLinkApplyPlan(rowNumber, StreamingLinkApplyAction.Unchanged, albumId, songId, provider, link.Url);
        }

        var change = $"{existing.Url} ({existing.Source.Key()}) -> {link.Url}";
        return existing.Source == StreamingLinkSource.Manual && !overwriteManual
            ? new StreamingLinkApplyPlan(rowNumber, StreamingLinkApplyAction.ManualProtected, albumId, songId, provider, change)
            : new StreamingLinkApplyPlan(rowNumber, StreamingLinkApplyAction.Update, albumId, songId, provider, change, write);
    }

    /// <summary>Validates one row's ids, provider and URL. Returns either the parsed row or an invalid plan.</summary>
    private static ((int AlbumId, int? SongId, StreamingProvider Provider, ParsedStreamingLink Link)? Row, StreamingLinkApplyPlan? Plan) ParseRow(
        StreamingLinkCsvRow row)
    {
        if (!TryPositiveInt(row[AlbumIdColumn], out var albumId))
        {
            return (null, Invalid(row.RowNumber, null, null, null, "albumId must be a positive integer"));
        }

        int? songId = null;
        if (row[AlbumSongIdColumn].Length > 0)
        {
            if (!TryPositiveInt(row[AlbumSongIdColumn], out var parsedSongId))
            {
                return (null, Invalid(row.RowNumber, albumId, null, null, "albumSongId must be blank or a positive integer"));
            }

            songId = parsedSongId;
        }

        if (!TryProvider(row[ProviderColumn], out var provider))
        {
            return (null, Invalid(row.RowNumber, albumId, songId, null, "provider must be spotify or apple-music"));
        }

        if (row[CandidateUrlColumn].Length == 0)
        {
            return (null, Invalid(row.RowNumber, albumId, songId, provider, "approved but candidateUrl is empty"));
        }

        var kind = songId is null ? StreamingLinkKind.Album : StreamingLinkKind.Track;
        if (!StreamingLinkUrl.TryParse(row[CandidateUrlColumn], kind, out var link, out var error))
        {
            return (null, Invalid(row.RowNumber, albumId, songId, provider, error));
        }

        return link.Provider == provider
            ? ((albumId, songId, provider, link), null)
            : (null, Invalid(row.RowNumber, albumId, songId, provider, $"candidateUrl is from {link.Provider.DisplayName()} but provider is {provider.Key()}"));
    }

    private static async Task<int> ExecuteAsync(
        IReadOnlyList<StreamingLinkApplyPlan> plans,
        IAdminDiscographyRepository repository,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var failed = 0;
        foreach (var plan in plans.Where(plan => plan.Write is not null))
        {
            try
            {
                if (plan.AlbumSongId is { } songId)
                {
                    await repository.SetSongStreamingLinkAsync(songId, plan.Provider!.Value, plan.Write, cancellationToken);
                }
                else
                {
                    await repository.SetAlbumStreamingLinkAsync(plan.AlbumId!.Value, plan.Provider!.Value, plan.Write, cancellationToken);
                }
            }
            catch (InvalidOperationException ex)
            {
                failed++;
                await output.WriteLineAsync($"Row {plan.RowNumber}: failed for {plan.Target}: {ex.Message}");
            }
        }

        return failed;
    }

    private static async Task WriteSummaryAsync(
        TextWriter output,
        ApplyStreamingLinksOptions options,
        int notApproved,
        IReadOnlyList<StreamingLinkApplyPlan> plans,
        int failed)
    {
        int Count(StreamingLinkApplyAction action) => plans.Count(plan => plan.Action == action);

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Approved rows: {plans.Count}");
        await output.WriteLineAsync($"Not approved (ignored): {notApproved}");
        await output.WriteLineAsync($"Insert: {Count(StreamingLinkApplyAction.Insert)}");
        await output.WriteLineAsync($"Update: {Count(StreamingLinkApplyAction.Update)}");
        await output.WriteLineAsync($"Unchanged: {Count(StreamingLinkApplyAction.Unchanged)}");
        await output.WriteLineAsync($"Skipped manual links: {Count(StreamingLinkApplyAction.ManualProtected)}");
        await output.WriteLineAsync($"Invalid: {Count(StreamingLinkApplyAction.Invalid)}");
        if (!options.Apply)
        {
            await output.WriteLineAsync("Dry run only. No database changes were made. Re-run with --apply to write.");
            return;
        }

        await output.WriteLineAsync($"Failed: {failed}");
        await output.WriteLineAsync(
            "Public discography pages are cached in the web app for up to 30 minutes. To show the links sooner, "
            + "save any album on /admin/discography (that clears the cache) or restart the web app.");
    }

    internal static string Describe(StreamingLinkApplyPlan plan)
    {
        var provider = plan.Provider?.Key() ?? "?";
        var verb = plan.Action switch
        {
            StreamingLinkApplyAction.Insert => "insert",
            StreamingLinkApplyAction.Update => "update",
            StreamingLinkApplyAction.Unchanged => "skip (unchanged)",
            StreamingLinkApplyAction.ManualProtected => "skip (manual link; pass --overwrite-manual to replace)",
            _ => "skip (invalid)",
        };
        return $"Row {plan.RowNumber}: {verb} {plan.Target} {provider}: {plan.Detail}";
    }

    private static StreamingLinkApplyPlan Invalid(int rowNumber, int? albumId, int? songId, StreamingProvider? provider, string reason) =>
        new(rowNumber, StreamingLinkApplyAction.Invalid, albumId, songId, provider, reason);

    private static bool TryPositiveInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;

    private static bool TryProvider(string value, out StreamingProvider provider)
    {
        var match = StreamingProviders.All.Where(candidate => string.Equals(candidate.Key(), value, StringComparison.OrdinalIgnoreCase)).ToList();
        provider = match.FirstOrDefault();
        return match.Count == 1;
    }

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- apply-streaming-links --file <file.csv> [options]",
            "",
            "Dry run by default: prints what would be inserted, updated or skipped.",
            "",
            "Options:",
            "  --apply                          Write the changes",
            "  --overwrite-manual               Replace links an admin entered by hand",
            "  --connection-string <value>      Or ConnectionStrings__QueenZoneLegacy / appsettings.Local.json",
            "  --settings-file <path>",
            "",
            "Only rows with approved=yes are applied. See docs/architecture/streaming-links-backfill.md.");
}

internal sealed class ApplyStreamingLinksOptions
{
    private ApplyStreamingLinksOptions()
    {
    }

    public string FilePath { get; private init; } = string.Empty;

    public string ConnectionString { get; private init; } = string.Empty;

    public bool Apply { get; private init; }

    public bool OverwriteManual { get; private init; }

    public bool IsValid { get; private init; }

    public string ErrorMessage { get; private init; } = string.Empty;

    public CancellationToken CancellationToken { get; init; }

    public static ApplyStreamingLinksOptions Parse(string[] args)
    {
        string? file = null;
        string? connectionString = null;
        string? storageConnectionString = null;
        string? settingsFile = null;
        var apply = false;
        var overwriteManual = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (ToolArgs.TryReadValue(args, ref index, "--file", out var fileValue))
            {
                file = fileValue;
            }
            else if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
            }
            else if (string.Equals(arg, "--overwrite-manual", StringComparison.OrdinalIgnoreCase))
            {
                overwriteManual = true;
            }
            else if (!ToolArgs.TryReadCommonOption(args, ref index, ref connectionString, ref storageConnectionString, ref settingsFile))
            {
                return Invalid($"Unsupported or incomplete argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            return Invalid("--file is required.");
        }

        var resolved = ToolArgs.ResolveLegacyConnectionString(connectionString, settingsFile);
        if (resolved is null)
        {
            return Invalid("--connection-string, ConnectionStrings__QueenZoneLegacy, or appsettings.Local.json is required (the dry run reads current links).");
        }

        return new ApplyStreamingLinksOptions
        {
            FilePath = file,
            ConnectionString = resolved,
            Apply = apply,
            OverwriteManual = overwriteManual,
            IsValid = true,
        };
    }

    private static ApplyStreamingLinksOptions Invalid(string message) => new() { ErrorMessage = message, IsValid = false };
}
