namespace QueenZone.Data;

/// <summary>
/// SQL shapes for <see cref="EfFreddieTributeRepository"/>.
/// The visible-thought predicate still trims, because legacy <c>FREDDIE_T.Thought</c>
/// padding was not checked against live rows. Switching the count to <c>Thought &lt;&gt; ''</c>
/// could drop space-padded imports.
/// </summary>
public static class FreddieTributeSql
{
    public const string VisibleThoughtPredicate =
        "DISPLAY = 1 AND NULLIF(LTRIM(RTRIM(ISNULL(Thought, ''))), '') IS NOT NULL";

    public const string SelectList = """
        ID AS Id,
        LTRIM(RTRIM(ISNULL(Name, 'Anonymous'))) AS Name,
        LTRIM(RTRIM(ISNULL(Thought, ''))) AS Thought,
        NULLIF(LTRIM(RTRIM(ISNULL(Country, ''))), '') AS Country,
        LTRIM(RTRIM(ISNULL(Freddie_Date, ''))) AS DateText,
        NULLIF(LTRIM(RTRIM(ISNULL(Freddie_Time, ''))), '') AS TimeText
        """;

    public static readonly string PageSql =
        "SELECT " + SelectList + """

        FROM dbo.FREDDIE_T
        WHERE 
        """ + VisibleThoughtPredicate + """

        ORDER BY ID DESC
        OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY
        """;

    public static readonly string CountSql =
        """
        SELECT COUNT(*) AS Value
        FROM dbo.FREDDIE_T
        WHERE 
        """ + VisibleThoughtPredicate;

    /// <summary>Inclusive id bounds for visible tributes. No sort of the table.</summary>
    public static readonly string IdBoundsSql =
        """
        SELECT MIN(ID) AS MinId, MAX(ID) AS MaxId
        FROM dbo.FREDDIE_T
        WHERE 
        """ + VisibleThoughtPredicate;

    /// <summary>Parameter: target id. Next visible tribute at or after that id.</summary>
    public static readonly string IdSeekAtOrAfterSql =
        """
        SELECT TOP (1) ID AS Value
        FROM dbo.FREDDIE_T
        WHERE 
        """ + VisibleThoughtPredicate + """
         AND ID >= {0}
        ORDER BY ID
        """;

    /// <summary>Parameter: target id. Previous visible tribute before that id (range wrap).</summary>
    public static readonly string IdSeekBeforeSql =
        """
        SELECT TOP (1) ID AS Value
        FROM dbo.FREDDIE_T
        WHERE 
        """ + VisibleThoughtPredicate + """
         AND ID < {0}
        ORDER BY ID DESC
        """;

    /// <summary>Parameter: tribute id.</summary>
    public static readonly string ByIdSql =
        "SELECT " + SelectList + """

        FROM dbo.FREDDIE_T
        WHERE ID = {0} AND 
        """ + VisibleThoughtPredicate;
}
