using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using QueenZone.Data;

namespace QueenZone.Tools;

/// <summary>An approved CSV row after validation.</summary>
internal sealed record ApprovedStreamingLink(int RowNumber, int AlbumId, int? AlbumSongId, ParsedStreamingLink Link);

internal enum StreamingLinkPlanAction
{
    Insert,
    Update,
    Unchanged,
    SkippedManual,
}

/// <summary>What applying one approved row would do, given the links stored now.</summary>
internal sealed record StreamingLinkPlanItem(ApprovedStreamingLink Row, StreamingLinkPlanAction Action, string? CurrentUrl);

/// <summary>
/// <c>apply-streaming-links</c>: imports rows a reviewer marked <c>approved=yes</c> in a
/// <c>suggest-streaming-links</c> CSV. Dry run by default; <c>--apply</c> writes with
/// <c>Source = imported</c>. Manual links are kept unless <c>--overwrite-manual</c> is passed.
/// Every approved row must validate before anything is written, and re-running a file is a no-op.
/// See <c>docs/architecture/streaming-links-backfill.md</c>.
/// </summary>
internal static class ApplyStreamingLinksCommand
{
    public const string UpdatedBy = "apply-streaming-links";

    private static readonly string[] RequiredColumns = ["albumId", "albumSongId", "provider", "candidateUrl", "approved"];

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
        using var reader = new StreamReader(options.FilePath, detectEncodingFromByteOrderMarks: true);
        return await RunAsync(options, new EfAdminDiscographyRepository(dbContext), reader);
    }

    internal static async Task<int> RunAsync(
        ApplyStreamingLinksOptions options,
        IAdminDiscographyRepository repository,
        TextReader csv)
    {
        var (rows, notApproved, errors) = ReadApproved(csv);
        var plan = errors.Count == 0 ? await PlanAsync(rows, repository, options.OverwriteManual, errors, options.CancellationToken) : [];
        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync($"{errors.Count} problem(s) in approved rows. Nothing was written:");
            foreach (var error in errors)
            {
                await Console.Error.WriteLineAsync($"  {error}");
            }

            return 1;
        }

        foreach (var item in plan.Where(item => item.Action is StreamingLinkPlanAction.Insert or StreamingLinkPlanAction.Update or StreamingLinkPlanAction.SkippedManual))
        {
            Console.WriteLine(Describe(item));
        }

        if (options.Apply)
        {
            foreach (var item in plan.Where(item => item.Action is StreamingLinkPlanAction.Insert or StreamingLinkPlanAction.Update))
            {
                var write = new StreamingLinkWrite(item.Row.Link, StreamingLinkSource.Imported, UpdatedBy);
                if (item.Row.AlbumSongId is int songId)
                {
                    await repository.SetSongStreamingLinkAsync(songId, item.Row.Link.Provider, write, options.CancellationToken);
                }
                else
                {
                    await repository.SetAlbumStreamingLinkAsync(item.Row.AlbumId, item.Row.Link.Provider, write, options.CancellationToken);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Approved rows: {rows.Count} (not approved: {notApproved})");
        Console.WriteLine($"{(options.Apply ? "Inserted" : "Would insert")}: {Count(plan, StreamingLinkPlanAction.Insert)}");
        Console.WriteLine($"{(options.Apply ? "Updated" : "Would update")}: {Count(plan, StreamingLinkPlanAction.Update)}");
        Console.WriteLine($"Unchanged: {Count(plan, StreamingLinkPlanAction.Unchanged)}");
        Console.WriteLine($"Skipped (manual link kept): {Count(plan, StreamingLinkPlanAction.SkippedManual)}");
        if (!options.Apply)
        {
            Console.WriteLine("Dry run only. Re-run with --apply to write these changes.");
        }
        else if (plan.Any(item => item.Action is StreamingLinkPlanAction.Insert or StreamingLinkPlanAction.Update))
        {
            Console.WriteLine("Public discography pages pick these up when their cache expires (up to 30 minutes).");
        }

        return 0;
    }

    /// <summary>Approved rows, the count of other rows, and validation errors (row-numbered).</summary>
    internal static (IReadOnlyList<ApprovedStreamingLink> Rows, int NotApproved, List<string> Errors) ReadApproved(TextReader csv)
    {
        var rows = new List<ApprovedStreamingLink>();
        var errors = new List<string>();
        var notApproved = 0;
        using var parser = new TextFieldParser(csv) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var header = parser.ReadFields();
        if (header is null)
        {
            errors.Add("The CSV file is empty.");
            return (rows, notApproved, errors);
        }

        // Spreadsheets may reorder or add columns and prepend a BOM; match by name.
        var columns = header
            .Select((name, index) => (Name: name.Trim().TrimStart('﻿'), index))
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredColumns.Where(name => !columns.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            errors.Add($"Missing column(s): {string.Join(", ", missing)}. Use a CSV written by suggest-streaming-links.");
            return (rows, notApproved, errors);
        }

        var rowNumber = 1;
        var slots = new Dictionary<(int, int?, StreamingProvider), int>();
        while (!parser.EndOfData)
        {
            rowNumber++;
            var fields = parser.ReadFields() ?? [];
            string Field(string name) => columns[name] < fields.Length ? fields[columns[name]].Trim() : string.Empty;
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (!string.Equals(Field("approved"), "yes", StringComparison.OrdinalIgnoreCase))
            {
                notApproved++;
                continue;
            }

            var row = ValidateRow(rowNumber, Field("albumId"), Field("albumSongId"), Field("provider"), Field("candidateUrl"), errors);
            if (row is null)
            {
                continue;
            }

            var slot = (row.AlbumId, row.AlbumSongId, row.Link.Provider);
            if (slots.TryGetValue(slot, out var firstRow))
            {
                errors.Add($"Row {rowNumber}: approves a second {row.Link.Provider.Key()} link for the same {(row.AlbumSongId is null ? "album" : "track")} as row {firstRow}.");
                continue;
            }

            slots[slot] = rowNumber;
            rows.Add(row);
        }

        return (rows, notApproved, errors);
    }

    private static ApprovedStreamingLink? ValidateRow(
        int rowNumber,
        string albumIdText,
        string albumSongIdText,
        string providerText,
        string url,
        List<string> errors)
    {
        if (!int.TryParse(albumIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var albumId) || albumId < 1)
        {
            errors.Add($"Row {rowNumber}: albumId '{albumIdText}' is not a positive number.");
            return null;
        }

        int? albumSongId = null;
        if (albumSongIdText.Length > 0)
        {
            if (!int.TryParse(albumSongIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var songId) || songId < 1)
            {
                errors.Add($"Row {rowNumber}: albumSongId '{albumSongIdText}' is not a positive number.");
                return null;
            }

            albumSongId = songId;
        }

        StreamingProvider provider;
        try
        {
            provider = StreamingProviders.FromKey(providerText.ToLowerInvariant());
        }
        catch (ArgumentOutOfRangeException)
        {
            errors.Add($"Row {rowNumber}: provider '{providerText}' must be spotify or apple-music.");
            return null;
        }

        var kind = albumSongId is null ? StreamingLinkKind.Album : StreamingLinkKind.Track;
        if (!StreamingLinkUrl.TryParse(url, kind, out var link, out var error))
        {
            errors.Add($"Row {rowNumber}: {(url.Length == 0 ? "approved but has no candidateUrl." : error)}");
            return null;
        }

        if (link.Provider != provider)
        {
            errors.Add($"Row {rowNumber}: candidateUrl is a {link.Provider.DisplayName()} link but provider is {provider.Key()}.");
            return null;
        }

        return new ApprovedStreamingLink(rowNumber, albumId, albumSongId, link);
    }

    internal static async Task<IReadOnlyList<StreamingLinkPlanItem>> PlanAsync(
        IReadOnlyList<ApprovedStreamingLink> rows,
        IAdminDiscographyRepository repository,
        bool overwriteManual,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var albums = new Dictionary<int, AdminAlbum?>();
        var plan = new List<StreamingLinkPlanItem>();
        foreach (var row in rows)
        {
            if (!albums.TryGetValue(row.AlbumId, out var album))
            {
                album = await repository.GetAlbumAsync(row.AlbumId, cancellationToken);
                albums[row.AlbumId] = album;
            }

            if (album is null)
            {
                errors.Add($"Row {row.RowNumber}: album {row.AlbumId} does not exist.");
                continue;
            }

            IReadOnlyList<AdminStreamingLink> stored;
            if (row.AlbumSongId is int songId)
            {
                var song = album.Songs.FirstOrDefault(candidate => candidate.SongId == songId);
                if (song is null)
                {
                    errors.Add($"Row {row.RowNumber}: song {songId} is not on album {row.AlbumId} ({album.Name}).");
                    continue;
                }

                stored = song.StreamingLinks;
            }
            else
            {
                stored = album.StreamingLinks;
            }

            var current = stored.FirstOrDefault(link => link.Provider == row.Link.Provider);
            var action = current switch
            {
                null => StreamingLinkPlanAction.Insert,
                _ when string.Equals(current.Url, row.Link.Url, StringComparison.Ordinal) => StreamingLinkPlanAction.Unchanged,
                { Source: StreamingLinkSource.Manual } when !overwriteManual => StreamingLinkPlanAction.SkippedManual,
                _ => StreamingLinkPlanAction.Update,
            };
            plan.Add(new StreamingLinkPlanItem(row, action, current?.Url));
        }

        return plan;
    }

    private static string Describe(StreamingLinkPlanItem item)
    {
        var target = item.Row.AlbumSongId is int songId
            ? $"album {item.Row.AlbumId} song {songId}"
            : $"album {item.Row.AlbumId}";
        var provider = item.Row.Link.Provider.Key();
        return item.Action switch
        {
            StreamingLinkPlanAction.Insert => $"insert  {target} {provider}: {item.Row.Link.Url}",
            StreamingLinkPlanAction.Update => $"update  {target} {provider}: {item.CurrentUrl} -> {item.Row.Link.Url}",
            _ => $"skip    {target} {provider}: manual link kept ({item.CurrentUrl}); pass --overwrite-manual to replace",
        };
    }

    private static int Count(IEnumerable<StreamingLinkPlanItem> plan, StreamingLinkPlanAction action) =>
        plan.Count(item => item.Action == action);

    private static void WriteUsage(string? errorMessage) =>
        ToolArgs.WriteUsage(
            errorMessage,
            "Usage:",
            "  dotnet run --project src/QueenZone.Tools -- apply-streaming-links --file <file.csv> [--apply] [--overwrite-manual]",
            "",
            "Dry run by default. Only rows with approved=yes are applied, as Source=imported.",
            "Manual links are kept unless --overwrite-manual is passed.",
            "  --connection-string <value>      Or ConnectionStrings__QueenZoneLegacy / appsettings.Local.json",
            "  --settings-file <path>");
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
            else if (ToolArgs.TryReadCommonOption(args, ref index, ref connectionString, ref storageConnectionString, ref settingsFile))
            {
                // Handled.
            }
            else if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
            }
            else if (string.Equals(arg, "--overwrite-manual", StringComparison.OrdinalIgnoreCase))
            {
                overwriteManual = true;
            }
            else
            {
                return Invalid($"Unsupported or incomplete argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            return Invalid("--file is required.");
        }

        connectionString ??= Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var settings = ToolsLocalSettings.TryLoad(settingsFile);
            connectionString = settings?.QueenZoneLegacy ?? settings?.QueenZoneLegacyLive;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Invalid("--connection-string, ConnectionStrings__QueenZoneLegacy, or appsettings.Local.json is required.");
        }

        return new ApplyStreamingLinksOptions
        {
            FilePath = file,
            ConnectionString = connectionString,
            Apply = apply,
            OverwriteManual = overwriteManual,
            IsValid = true,
        };
    }

    private static ApplyStreamingLinksOptions Invalid(string message) => new() { ErrorMessage = message, IsValid = false };
}
