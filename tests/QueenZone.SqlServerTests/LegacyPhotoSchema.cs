namespace QueenZone.SqlServerTests;

/// <summary>
/// Legacy picture tables as they exist on the <c>queenzone_legacy_sync</c> mirror (column types
/// read from <c>sys.columns</c>), including the persisted <c>PIC_LONGEST_SIDE</c> column added by
/// the <c>AddPicFilesLongestSide</c> migration. Used by the photo repository tests (#1672).
/// </summary>
internal static class LegacyPhotoSchema
{
    public const string CreateTablesSql = """
        CREATE TABLE dbo.PIC_CAT_T
        (
            PIC_CAT_ID tinyint IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Cat_ID int NOT NULL,
            Paths nvarchar(100) NULL,
            BaseUrl nvarchar(100) NULL,
            Name nvarchar(100) NULL,
            thedate datetime NULL
        );

        CREATE TABLE dbo.PIC_FILES_T
        (
            PIC_ID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Name varchar(150) NULL,
            Cat_ID int NULL,
            Date_time datetime NOT NULL,
            Url varchar(400) NULL,
            Thumb_URL varchar(255) NULL,
            t_height int NULL,
            t_width int NULL,
            user_id int NULL,
            DISPLAY int NULL,
            PIC_HEIGHT smallint NOT NULL,
            PIC_WIDTH smallint NOT NULL,
            KEYWORDS varchar(1000) NULL,
            PICTURE_YEAR smallint NULL,
            PIC_LONGEST_SIDE AS (
                CAST(
                    CASE
                        WHEN ISNULL(PIC_WIDTH, 0) > 0 AND ISNULL(PIC_HEIGHT, 0) > 0
                            THEN CASE WHEN PIC_WIDTH > PIC_HEIGHT THEN PIC_WIDTH ELSE PIC_HEIGHT END
                        ELSE 0
                    END
                AS int)
            ) PERSISTED
        );

        INSERT INTO dbo.PIC_CAT_T (Cat_ID, Name) VALUES (9, N'Brian May'), (11, N'Roger Taylor');
        """;
}
