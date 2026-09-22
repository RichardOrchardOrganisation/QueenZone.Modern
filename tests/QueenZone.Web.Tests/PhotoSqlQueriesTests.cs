using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoSqlQueriesTests
{
    [Fact]
    public void CreateProduction_ReturnsSeekFriendlyCategoryAndNeighborSql()
    {
        var sql = PhotoSqlQueries.CreateProduction();

        Assert.Contains("GROUP BY c.cat_id, c.name", sql.CategoriesWithCountsSql, StringComparison.Ordinal);
        Assert.Contains("OFFSET {0} ROWS FETCH NEXT {1} ROWS ONLY", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql.PreviousPicIdSql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql.NextPicIdSql, StringComparison.Ordinal);
        Assert.Contains("Date_time > {1}", sql.IndexBeforeSql, StringComparison.Ordinal);
        Assert.Contains("Date_time = {1} AND PIC_ID > {2}", sql.IndexBeforeSql, StringComparison.Ordinal);
        Assert.Contains("AS PreviousPicId", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("AS NextPicId", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("AS TotalCount", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("AS IndexBefore", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("WHERE p.Cat_ID = {0} AND p.PIC_ID = {1} AND p.DISPLAY = 1", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY c.name, p.Date_time DESC, p.PIC_ID DESC", sql.SitemapSql, StringComparison.Ordinal);
        Assert.Contains("WHERE p.Cat_ID = {0} AND p.DISPLAY = 1", sql.CategoryAllSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", sql.RandomIdBoundsSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", sql.RandomIdSeekBeforeSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", sql.PublishedByIdsSql, StringComparison.Ordinal);
        Assert.Contains("MIN(p.PIC_ID) AS MinId", sql.RandomIdBoundsSql, StringComparison.Ordinal);
        Assert.Contains("MAX(p.PIC_ID) AS MaxId", sql.RandomIdBoundsSql, StringComparison.Ordinal);
        Assert.Contains("p.PIC_ID >= {1}", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY p.PIC_ID", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.DoesNotContain("ORDER BY p.PIC_ID, p.Date_time", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("p.PIC_ID IN ({ID_LIST})", sql.PublishedByIdsSql, StringComparison.Ordinal);
        var byIds = PhotoSqlQueries.ApplyIdList(sql.PublishedByIdsSql, [11, 12]);
        Assert.Contains("IN (11, 12)", byIds, StringComparison.Ordinal);
        Assert.DoesNotContain("{ID_LIST}", byIds, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PhotoSqlQueries.ApplyIdList(sql.PublishedByIdsSql, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoSqlQueries.ApplyIdList(sql.PublishedByIdsSql, [0]));
        Assert.Contains("CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT", sql.CategoryAllSql, StringComparison.Ordinal);
        Assert.Contains("AS submitted_by_display_name", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN dbo.USERS_T u ON u.USER_ID = p.user_id", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("MemberAccounts m", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("PhotoSubmissions ps", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("ps.PromotedPicId = p.PIC_ID", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("PhotoSubmissions ps", sql.PhotoByIdSql, StringComparison.Ordinal);
        Assert.Contains("{PHOTO_FILTER_P}", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("{PHOTO_FILTER_T}", sql.DetailNavigationSql, StringComparison.Ordinal);
        var filtered = sql.ApplyFilter(sql.CategoryCountSql, new PhotoListFilter(PhotoSizePreset.Desktop));
        Assert.DoesNotContain("{PHOTO_FILTER_P}", filtered, StringComparison.Ordinal);
        Assert.Contains("p.PIC_WIDTH >= 1920", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(ISNULL(p.PIC_WIDTH, 0) AS int) >", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("CAST(ISNULL(", filtered.Replace("CAST(ISNULL(p.PIC_WIDTH, 0) AS int) AS PIC_WIDTH", "", StringComparison.Ordinal).Replace("CAST(ISNULL(p.PIC_HEIGHT, 0) AS int) AS PIC_HEIGHT", "", StringComparison.Ordinal), StringComparison.Ordinal);

        var longest = sql.ApplyFilter(sql.DetailNavigationSql, new PhotoListFilter(PhotoSizePreset.Hd));
        Assert.Contains("t.PIC_LONGEST_SIDE >= 1280", longest, StringComparison.Ordinal);
        Assert.Contains("p.PIC_LONGEST_SIDE >= 1280", longest, StringComparison.Ordinal);
        Assert.DoesNotContain("CASE WHEN", longest, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSqliteFixture_ReturnsCompatibleShapes()
    {
        var sql = PhotoSqlQueries.CreateSqliteFixture();

        Assert.Contains("LIMIT {1} OFFSET {0}", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("UNION ALL", sql.PreviousPicIdSql, StringComparison.Ordinal);
        Assert.Contains("FROM PhotoItems", sql.CategoryCountSql, StringComparison.Ordinal);
        Assert.Contains("AS PreviousPicId", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("AS NextPicId", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("PIC_WIDTH", sql.CategoryPageSql, StringComparison.Ordinal);
        Assert.Contains("PIC_HEIGHT", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("submitted_by_display_name", sql.DetailNavigationSql, StringComparison.Ordinal);
        Assert.Contains("MIN(p.pic_id) AS MinId", sql.RandomIdBoundsSql, StringComparison.Ordinal);
        Assert.Contains("p.pic_id >= {1}", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.DoesNotContain("RANDOM()", sql.RandomIdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("p.pic_id IN ({ID_LIST})", sql.PublishedByIdsSql, StringComparison.Ordinal);
    }
}
