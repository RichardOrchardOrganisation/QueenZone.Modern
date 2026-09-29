using QueenZone.Data;

namespace QueenZone.Web.Tests;

// The search SQL is a hand-written script that only runs on SQL Server, which these tests can't reach.
// Its text is therefore the contract: each test slices one branch of the script and pins its shape.
public sealed class SearchDocumentSearchSqlTests
{
    [Fact]
    public void Rank_cap_matches_the_sql_source_of_truth()
    {
        Assert.Equal(1000, SiteSearchLimits.MaxRankedMatches);
        Assert.Equal(5000, SiteSearchLimits.TypedMatchScanLimit);
        Assert.Contains(
            "@RankLimit      INT = 1000",
            ReadSqlSourceOfTruth(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Untyped_all_keeps_the_global_freetext_rank_cap()
    {
        var untypedBranch = ReadUntypedMatchInsert();

        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)",
            untypedBranch,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT TOP (@MatchLimit)", untypedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("d.ContentType = @ContentType", untypedBranch, StringComparison.Ordinal);
        Assert.Contains("d.ContentType <> N'tribute'", untypedBranch, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]", untypedBranch, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(
                ReadSqlSourceOfTruth(),
                "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)"));
    }

    [Fact]
    public void Typed_search_applies_rank_cap_after_content_type_filter()
    {
        var typedBranch = ReadTypedMatchInsert();
        var joinIndex = typedBranch.IndexOf(
            "INNER JOIN dbo.SearchDocument d ON d.Id = ft.[KEY]",
            StringComparison.Ordinal);
        var filterIndex = typedBranch.IndexOf("WHERE  d.ContentType = @ContentType", StringComparison.Ordinal);
        var orderIndex = typedBranch.IndexOf("ORDER BY ft.[RANK] DESC", StringComparison.Ordinal);

        Assert.Contains("SELECT TOP (@MatchLimit)", typedBranch, StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit)",
            typedBranch,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)",
            typedBranch,
            StringComparison.Ordinal);
        Assert.True(joinIndex >= 0, "Typed search must join SearchDocument before capping.");
        Assert.True(filterIndex > joinIndex, "Typed search must filter ContentType after the join.");
        Assert.True(orderIndex > filterIndex, "Typed search must apply TOP after the ContentType filter.");
        Assert.Contains("d.ContentType <> N'tribute'", typedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("[RANK] *", typedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("CONTAINSTABLE", typedBranch, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (@MatchLimit)", typedBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_search_candidate_scan_has_a_finite_independent_cap()
    {
        var sql = ReadSqlSourceOfTruth();

        Assert.Contains("@TypedRankLimit INT = 5000", sql, StringComparison.Ordinal);
        Assert.Contains("DECLARE @TypedMatchLimit INT = CASE", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN @TypedRankLimit > 5000 THEN 5000", sql, StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit)",
            ReadTypedMatchInsert(),
            StringComparison.Ordinal);
        Assert.Contains("SiteSearchLimits.TypedMatchScanLimit", ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Repositories", "EfSiteSearchService.cs")));
    }

    [Fact]
    public void Both_freetexttable_match_inserts_recompile()
    {
        var untypedBranch = ReadUntypedMatchInsert();
        var typedBranch = ReadTypedMatchInsert();

        Assert.Contains("OPTION (RECOMPILE)", untypedBranch, StringComparison.Ordinal);
        Assert.Contains("OPTION (RECOMPILE)", typedBranch, StringComparison.Ordinal);

        var migration = ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Migrations", "20260914080000_RecompileSearchDocumentSearchMatches.cs"));
        var upStart = migration.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downStart = migration.IndexOf("protected override void Down", StringComparison.Ordinal);
        Assert.True(upStart >= 0 && downStart > upStart, "Expected Up before Down.");
        Assert.Equal(2, CountOccurrences(migration[upStart..downStart], "OPTION (RECOMPILE)"));
        Assert.DoesNotContain("OPTION (RECOMPILE)", migration[downStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void SearchDocument_fts_uses_auto_change_tracking_not_a_sync_rebuild()
    {
        var migration = ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Migrations", "20260804113500_AddSearchDocumentFullTextSearch.cs"));

        Assert.Contains("WITH CHANGE_TRACKING AUTO", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CHANGE_TRACKING OFF", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("CHANGE_TRACKING MANUAL", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("START FULL POPULATION", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("START UPDATE POPULATION", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Article_search_sync_is_single_row_upsert_not_a_content_type_replace()
    {
        var upsert = ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Repositories", "EfSearchIndexService.cs"));
        var status = ReadRepoFile(
            Path.Combine("src", "QueenZone.Web", "Pages", "Admin", "Articles", "Status.cshtml.cs"));
        var action = ReadRepoFile(
            Path.Combine("src", "QueenZone.Web", "Pages", "Admin", "Articles", "Action.cshtml.cs"));
        var upsertMethod = upsert[upsert.IndexOf("public async Task UpsertAsync", StringComparison.Ordinal)..];
        upsertMethod = upsertMethod[..upsertMethod.IndexOf("public async Task RemoveAsync", StringComparison.Ordinal)];

        Assert.Contains("searchIndexService.UpsertAsync", status, StringComparison.Ordinal);
        Assert.Contains("searchIndexService.UpsertAsync", action, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceContentTypeAsync", status, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceContentTypeAsync", action, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransactionAsync", upsertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("FULLTEXT", upsertMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("POPULATION", upsertMethod, StringComparison.Ordinal);
        Assert.Equal(30, QueenZoneSqlServerOptions.DefaultCommandTimeoutSeconds);
        Assert.Equal(1000, SiteSearchLimits.MaxRankedMatches);
    }

    [Fact]
    public void Historical_cap_migration_embeds_the_global_rank_cap()
    {
        var migration = ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Migrations", "20260827143000_CapSearchDocumentSearchMatches.cs"));

        Assert.Contains("@RankLimit    INT = 1000", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE #Matches", migration, StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("SiteSearchLimits.MaxRankedMatches", ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Repositories", "EfSiteSearchService.cs")));
    }

    [Fact]
    public void Migration_embeds_typed_cap_after_content_type_filter()
    {
        var migration = ReadRepoFile(
            Path.Combine("src", "QueenZone.Data", "Migrations", "20260908140000_CapTypedSearchAfterContentTypeFilter.cs"));
        var sql = ReadSqlSourceOfTruth();

        Assert.Contains("IF @ContentType IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (@MatchLimit)", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE  d.ContentType = @ContentType", migration, StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)",
            migration,
            StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query)",
            migration,
            StringComparison.Ordinal);
        Assert.Contains("IF @ContentType IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT TOP (@MatchLimit)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceContentTypeAsync", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("START FULL POPULATION", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Latest_migration_embeds_the_finite_typed_candidate_cap()
    {
        var migration = ReadRepoFile(Path.Combine(
            "src", "QueenZone.Data", "Migrations", "20260922140000_CapTypedSearchFullTextCandidates.cs"));
        var upStart = migration.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downStart = migration.IndexOf("protected override void Down", StringComparison.Ordinal);
        Assert.True(upStart >= 0 && downStart > upStart, "Expected Up before Down.");

        var up = migration[upStart..downStart];
        Assert.Contains("@TypedRankLimit INT = 5000", up, StringComparison.Ordinal);
        Assert.Contains("WHEN @TypedRankLimit > 5000 THEN 5000", up, StringComparison.Ordinal);
        Assert.Contains("@Query, @TypedMatchLimit", up, StringComparison.Ordinal);
        Assert.Contains("OPTION (RECOMPILE)", up, StringComparison.Ordinal);
        Assert.DoesNotContain("@TypedRankLimit", migration[downStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void Latest_migration_excludes_tributes_at_matches_insert()
    {
        var migration = ReadRepoFile(Path.Combine(
            "src", "QueenZone.Data", "Migrations", "20260929140000_ExcludeTributesFromSiteSearch.cs"));
        var upStart = migration.IndexOf("protected override void Up", StringComparison.Ordinal);
        var downStart = migration.IndexOf("protected override void Down", StringComparison.Ordinal);
        Assert.True(upStart >= 0 && downStart > upStart, "Expected Up before Down.");

        var up = migration[upStart..downStart];
        Assert.Contains("DELETE FROM dbo.SearchDocument", up, StringComparison.Ordinal);
        Assert.Contains("ContentType IN (N'tribute', N'freddie-tribute')", up, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(up, "d.ContentType <> N'tribute'"));
        Assert.Equal(2, CountOccurrences(up, "OPTION (RECOMPILE)"));
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @MatchLimit)",
            up,
            StringComparison.Ordinal);
        Assert.Contains(
            "FREETEXTTABLE(dbo.SearchDocument, (Title, Body), @Query, @TypedMatchLimit)",
            up,
            StringComparison.Ordinal);
        Assert.Contains("@RankLimit      INT = 1000", up, StringComparison.Ordinal);
        Assert.Contains("@TypedRankLimit INT = 5000", up, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE #Page", up, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO #Matches (DocumentId, SearchRank, ContentType, PublishedAt)", up, StringComparison.Ordinal);
        Assert.Contains("SELECT @TotalRecords = COUNT(*)", up, StringComparison.Ordinal);
        Assert.Contains("FROM   #Matches;", up, StringComparison.Ordinal);
        Assert.DoesNotContain("@ContentType IS NULL OR", up, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN #Matches", up, StringComparison.Ordinal);

        var sql = ReadSqlSourceOfTruth();
        Assert.Equal(2, CountOccurrences(sql, "d.ContentType <> N'tribute'"));
        Assert.DoesNotContain("d.ContentType <> N'tribute'", migration[downStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void Search_tail_pages_from_matches_then_joins_only_the_page()
    {
        var sql = ReadSqlSourceOfTruth();
        var tail = ReadSearchTail();

        Assert.Contains("ContentType NVARCHAR(50) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("PublishedAt DATETIMEOFFSET NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE #Page", tail, StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY SearchRank DESC, PublishedAt DESC, DocumentId DESC",
            tail,
            StringComparison.Ordinal);
        Assert.Contains(
            "ORDER BY p.SearchRank DESC, p.PublishedAt DESC, p.DocumentId DESC",
            tail,
            StringComparison.Ordinal);
        Assert.Contains("FROM   #Page p", tail, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN dbo.SearchDocument d ON d.Id = p.DocumentId", tail, StringComparison.Ordinal);
        Assert.Contains("SELECT @TotalRecords = COUNT(*)", tail, StringComparison.Ordinal);
        Assert.Contains("FROM   #Matches;", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("@ContentType IS NULL OR", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN #Matches", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", sql.Replace(tail, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static string ReadSqlSourceOfTruth() =>
        ReadRepoFile(Path.Combine("docs", "sql", "010-search-document-full-text-search.sql"));

    private static string ReadUntypedMatchInsert()
    {
        var sql = ReadSqlSourceOfTruth();
        var start = sql.IndexOf("IF @ContentType IS NULL", StringComparison.Ordinal);
        var end = sql.IndexOf("ELSE", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Expected an untyped IF @ContentType IS NULL branch.");
        return sql[start..end];
    }

    private static string ReadTypedMatchInsert()
    {
        var sql = ReadSqlSourceOfTruth();
        var ifIndex = sql.IndexOf("IF @ContentType IS NULL", StringComparison.Ordinal);
        var elseIndex = sql.IndexOf("ELSE", ifIndex, StringComparison.Ordinal);
        var beginIndex = sql.IndexOf("BEGIN", elseIndex, StringComparison.Ordinal);
        var endIndex = sql.IndexOf("END", beginIndex, StringComparison.Ordinal);
        Assert.True(elseIndex >= 0 && beginIndex > elseIndex && endIndex > beginIndex,
            "Expected a typed ELSE BEGIN/END match-insert branch.");
        return sql[elseIndex..endIndex];
    }

    private static string ReadSearchTail()
    {
        var sql = ReadSqlSourceOfTruth();
        var pageIndex = sql.IndexOf("CREATE TABLE #Page", StringComparison.Ordinal);
        Assert.True(pageIndex >= 0, "Expected a #Page materialization after the match inserts.");
        return sql[pageIndex..];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = RepoPaths.Combine(relativePath);
        Assert.True(File.Exists(path), $"Expected {relativePath} at {path}");
        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
