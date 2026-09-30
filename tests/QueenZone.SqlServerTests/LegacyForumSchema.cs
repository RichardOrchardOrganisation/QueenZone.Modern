using Microsoft.EntityFrameworkCore;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Scratch SQL Server objects for <see cref="LegacyForumRepository"/> (#1672 / #1891).
/// Column types come from the 2026-09-29 <c>queenzone_legacy_sync</c> dump of
/// <c>Q_FORUM_T</c> plus the 2026-09-30 QueenZoneLocal dump of
/// <c>Q_FORUM_TOPIC_T</c> / views (those topic objects are absent on the sync
/// database). Stored procedures are the sync <c>OBJECT_DEFINITION</c> text.
/// <c>USERS_T</c> includes only the columns the procedures read, typed as on
/// the #1890 member-lookup dump and <c>docs/legacy/db-schema.txt</c>.
/// </summary>
internal static class LegacyForumSchema
{
    public static readonly string[] InstallBatches =
    [
        "CREATE SCHEMA dbUser AUTHORIZATION dbo;",
        """
        CREATE TABLE dbo.Q_FORUM_T
        (
            Q_FORUM_ID int NOT NULL,
            Q_FORUM_NAME varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Q_FORUM_DESCRIPTION varchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            Q_FORUM_POST_COUNT int NULL,
            Q_FORUM_LAST_POST datetime NULL,
            FORUM_ORDER tinyint NULL,
            TITLE_WORDS varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT PK_Q_FORUM_T PRIMARY KEY NONCLUSTERED (Q_FORUM_ID) WITH (FILLFACTOR = 90)
        );
        """,
        """
        CREATE TABLE dbo.Q_FORUM_TOPIC_T
        (
            Q_FORUM_TOPIC_ID int IDENTITY(1,1) NOT NULL,
            Q_FORUM_ID tinyint NOT NULL,
            TOPIC_SUBJECT char(75) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            USER_ID int NULL,
            TOPIC_REPLIES smallint NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_TOPIC_REPLIES DEFAULT ((0)),
            TOPIC_LAST_POST smalldatetime NULL,
            TOPIC_DATE smalldatetime NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_TOPIC_DATE DEFAULT (getdate()),
            TOPIC_EDIT tinyint NULL,
            POST_IP char(20) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            NEWS_ID smallint NULL,
            Q_FORUM_TOPIC_PARENT_ID int NOT NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_Q_FORUM_TOPIC_PARENT_ID DEFAULT ((0)),
            TOPIC_MESSAGE varchar(8000) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            STICKY tinyint NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_STICKY DEFAULT ((0)),
            TOPIC_EDIT_DATE smalldatetime NULL,
            ATTACHMENT varchar(120) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            FILESIZE char(12) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            ATTACH_COUNT smallint NOT NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_ATTACH_COUNT DEFAULT ((0)),
            LAST_USER_ID int NULL,
            TOPIC_STARTER tinyint NOT NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_TOPIC_STARTER DEFAULT ((0)),
            DISCOGRAPHY tinyint NOT NULL
                CONSTRAINT DF_Q_FORUM_TOPIC_T_DISCOGRAPHY DEFAULT ((0)),
            INFOHASH varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT PK_Q_FORUM_TOPIC_T PRIMARY KEY NONCLUSTERED (Q_FORUM_TOPIC_ID) WITH (FILLFACTOR = 90)
        );
        """,
        """
        CREATE TABLE dbo.USERS_T
        (
            USER_ID int IDENTITY(1,1) NOT NULL,
            USERNAME char(40) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            SIGNATURE varchar(200) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            NUMBER_OF_POSTS int NOT NULL
                CONSTRAINT DF_USERS_T_NUMBER_OF_POSTS DEFAULT ((0)),
            DATE_CREATED smalldatetime NOT NULL
                CONSTRAINT DF_USERS_T_DATE_CREATED DEFAULT (getdate()),
            VALIDATED tinyint NOT NULL
                CONSTRAINT DF_USERS_T_VALIDATED DEFAULT ((0)),
            ONLINE_NOW tinyint NOT NULL
                CONSTRAINT DF_USERS_T_ONLINE_NOW DEFAULT ((0)),
            DISPLAY_MESSAGE varchar(130) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
            CONSTRAINT PK_USERS_T PRIMARY KEY CLUSTERED (USER_ID)
        );
        """,
        """
        CREATE TABLE dbo.Q_USERS_AVATAR_T
        (
            Q_USERS_AVATAR_ID int IDENTITY(1,1) NOT NULL,
            USER_ID int NOT NULL,
            AVATAR varchar(50) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,
            ACTIVE tinyint NOT NULL,
            DATE_CREATED smalldatetime NOT NULL
                CONSTRAINT DF_Q_USERS_AVATAR_T_DATE_CREATED DEFAULT (getdate()),
            CONSTRAINT PK_Q_USERS_AVATAR_T PRIMARY KEY CLUSTERED (Q_USERS_AVATAR_ID)
        );
        """,
        """
        CREATE TABLE dbo.Q_FORUM_MAIL_T
        (
            Q_FORUM_MAIL_Id int IDENTITY(1,1) NOT NULL,
            Q_FORUM_TOPIC_ID int NULL,
            USER_ID int NULL,
            CONSTRAINT PK_Q_FORUM_MAIL_T PRIMARY KEY NONCLUSTERED (Q_FORUM_MAIL_Id) WITH (FILLFACTOR = 90)
        );
        """,
        """
        CREATE VIEW dbUser.Q_FORUM_TOPIC_THREAD_COUNT_V
        AS
        SELECT     Q_FORUM_ID, COUNT(Q_FORUM_TOPIC_ID) AS THREADCOUNT
        FROM         dbo.Q_FORUM_TOPIC_T
        WHERE     (TOPIC_STARTER = 1) OR
                              (Q_FORUM_TOPIC_PARENT_ID = 0)
        GROUP BY Q_FORUM_ID
        """,
        """
        CREATE VIEW dbo.Q_FORUM_TOPIC_NO_PARENT_V
        AS
        SELECT     Q_FORUM_TOPIC_ID, Q_FORUM_ID, TOPIC_SUBJECT, USER_ID, TOPIC_REPLIES, TOPIC_LAST_POST, STICKY, LAST_USER_ID, ATTACHMENT, TOPIC_STARTER,
                              DISCOGRAPHY
        FROM         dbo.Q_FORUM_TOPIC_T
        WHERE     (TOPIC_STARTER = 1)
        """,
        """
        CREATE VIEW dbo.Q_USERS_AVATAR_V
        AS
        SELECT     USER_ID, AVATAR, ACTIVE
        FROM         dbo.Q_USERS_AVATAR_T
        WHERE     (ACTIVE = 1)
        """,
        """
        CREATE PROCEDURE [dbo].[Q_FORUM_VIEW_PAGE_SP]
        @CurrentPage As int,
        @PageSize As int,
        @Q_FORUM_ID TINYINT,
         @TotalRecords int OUTPUT
         AS

        SET NOCOUNT ON

        Declare @FirstRec int
        Declare @LastRec int

        Set @FirstRec = (@CurrentPage - 1) * @PageSize
        Set @LastRec = (@CurrentPage * @PageSize + 1)

        DECLARE @TempTable Table
        (
        ID int IDENTITY,
        Q_FORUM_TOPIC_ID INT,
        TOPIC_SUBJECT VARCHAR (150),
        TOPIC_LAST_POST SMALLDATETIME,
        USER_ID INT,
        USERNAME VARCHAR(100),
        NUMBEROFREPLIES SMALLINT,
        LAST_POST_USERNAME VARCHAR(100),
        STICKY TINYINT
        )

        INSERT INTO @TempTable
        (
        Q_FORUM_TOPIC_ID ,
        TOPIC_SUBJECT,
        TOPIC_LAST_POST,
        USER_ID,
        USERNAME ,
        NUMBEROFREPLIES,
        LAST_POST_USERNAME,
        STICKY

        )
        SELECT DISTINCT
                              dbo.Q_FORUM_TOPIC_NO_PARENT_V.Q_FORUM_TOPIC_ID,
        dbo.Q_FORUM_TOPIC_NO_PARENT_V.TOPIC_SUBJECT,
                              dbo.Q_FORUM_TOPIC_NO_PARENT_V.TOPIC_LAST_POST,
        dbo.USERS_T.USER_ID, dbo.USERS_T.USERNAME,
                              dbo.Q_FORUM_TOPIC_NO_PARENT_V.TOPIC_REPLIES AS NUMBEROFREPLIES,
         USERS_T_1.USERNAME AS LAST_POST_USERNAME,
                              dbo.Q_FORUM_TOPIC_NO_PARENT_V.STICKY
        FROM         dbo.USERS_T INNER JOIN
                              dbo.Q_FORUM_TOPIC_NO_PARENT_V ON dbo.USERS_T.USER_ID = dbo.Q_FORUM_TOPIC_NO_PARENT_V.USER_ID LEFT OUTER JOIN
                              dbo.USERS_T USERS_T_1 ON dbo.Q_FORUM_TOPIC_NO_PARENT_V.
        LAST_USER_ID = USERS_T_1.USER_ID
        WHERE Q_FORUM_ID = @Q_FORUM_ID and users_t.validated = 1
        order by STICKY DESC, TOPIC_LAST_POST desc



        SELECT
        id,
        Q_FORUM_TOPIC_ID ,
        TOPIC_SUBJECT,
        TOPIC_LAST_POST,
        USER_ID,
        USERNAME ,
        NUMBEROFREPLIES,
        LAST_POST_USERNAME,
        STICKY
        FROM @TempTable
        WHERE ID > @FirstRec AND ID < @LastRec
        order by STICKY DESC, TOPIC_LAST_POST desc




        SELECT @TotalRecords = Count(*)
        FROM       dbo.Q_FORUM_TOPIC_T
        WHERE topic_starter = 1 AND Q_FORUM_ID = @Q_FORUM_ID
        """,
        """
        CREATE PROCEDURE [dbo].[Q_FORUM_TOPIC_NEW_SP]
        	(
        	@CurrentPage As int,
        	@PageSize As int,
        	 @Q_FORUM_TOPIC_ID int,
        	@USER_ID int,
        	 @TotalRecords int OUTPUT,
        	@SUBSCRIBED INT OUTPUT,
        	@forum_name VARCHAR(30) OUTPUT,
        	@SUBJECT VARCHAR(75) OUTPUT,
        	@Q_FORUM_ID INT OUTPUT,
        @DISCO TINYINT OUTPUT
        	)
        AS
        SET NOCOUNT ON
        Declare @FirstRec int
        Declare @LastRec int
        Set @FirstRec = (@CurrentPage - 1) * @PageSize
        Set @LastRec = (@CurrentPage * @PageSize + 1)
        DECLARE @TempTable Table
        (

        	ID int IDENTITY,
        	TOPIC_MESSAGE varchar(8000),
        	TOPIC_DATE smalldatetime,
        	USER_ID int,
        	USERNAME char(40),
        	SIGNATURE varchar(200),
        	NUMBER_OF_POSTS smallint,
        	DATE_CREATED smalldatetime,
        	Q_FORUM_TOPIC_ID int,
        ATTACHMENT varchar(120),
        FILESIZE char(12),
        ATTACH_COUNT SMALLINT,
        ONLINE TINYINT,
        AVATAR VARCHAR(50),
        DISPLAY_MESSAGE varchar(30),
        DISCO TINYINT
        )
        INSERT INTO
        @TempTable
        (
        TOPIC_MESSAGE,
        TOPIC_DATE,
        USER_ID,
        USERNAME,
        SIGNATURE,
        NUMBER_OF_POSTS,
        DATE_CREATED,
        Q_FORUM_TOPIC_ID,
        ATTACHMENT,
        FILESIZE,
        ATTACH_COUNT,
        ONLINE,
        AVATAR,
        DISPLAY_MESSAGE,
        DISCO
        )
        SELECT distinct      dbo.Q_FORUM_TOPIC_T.TOPIC_MESSAGE, dbo.Q_FORUM_TOPIC_T.TOPIC_DATE, dbo.USERS_T.USER_ID, dbo.USERS_T.USERNAME,
                             isnull( dbo.USERS_T.SIGNATURE, '') as SIGNATURE, dbo.USERS_T.NUMBER_OF_POSTS, dbo.USERS_T.DATE_CREATED,
                              dbo.Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_ID, dbo.Q_FORUM_TOPIC_T.ATTACHMENT, dbo.Q_FORUM_TOPIC_T.FILESIZE,
                              dbo.Q_FORUM_TOPIC_T.ATTACH_COUNT, dbo.USERS_T.ONLINE_NOW, dbo.Q_USERS_AVATAR_V.AVATAR, DISPLAY_MESSAGE, DISCOGRAPHY
        FROM         dbo.USERS_T INNER JOIN
                              dbo.Q_FORUM_TOPIC_T ON dbo.USERS_T.USER_ID = dbo.Q_FORUM_TOPIC_T.USER_ID LEFT OUTER JOIN
                              dbo.Q_USERS_AVATAR_V ON dbo.USERS_T.USER_ID = dbo.Q_USERS_AVATAR_V.USER_ID
        where (Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_ID = @Q_FORUM_TOPIC_ID  or Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_PARENT_ID = @Q_FORUM_TOPIC_ID )
        AND DISCOGRAPHY <> 2
        and users_t.validated = 1
        ORDER BY TOPIC_DATE ASC
        SELECT

        isnull(TOPIC_MESSAGE, '') as TOPIC_MESSAGE ,
        	TOPIC_DATE ,
        	USER_ID,
        	USERNAME,
        	SIGNATURE,
        	NUMBER_OF_POSTS,
        	DATE_CREATED,
        	Q_FORUM_TOPIC_ID,
        ATTACHMENT,
        FILESIZE,
        ATTACH_COUNT,
        ONLINE,
        AVATAR,
        DISPLAY_MESSAGE,
        DISCO


        FROM @TempTable
        WHERE ID > @FirstRec AND ID < @LastRec
        order by TOPIC_DATE  asc
        Select
        @TotalRecords = Count(*)
        From
        Q_FORUM_TOPIC_T
        WHERE
        Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_ID = @Q_FORUM_TOPIC_ID  or Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_PARENT_ID = @Q_FORUM_TOPIC_ID


        SELECT
        @SUBSCRIBED = COUNT(Q_FORUM_MAIL_ID)
        FROM Q_FORUM_MAIL_T
        WHERE Q_FORUM_TOPIC_ID = @Q_FORUM_TOPIC_ID AND USER_ID = @USER_ID

        SELECT    @Q_FORUM_ID = dbo.Q_FORUM_TOPIC_T.Q_FORUM_ID, @forum_name = dbo.Q_FORUM_T.Q_FORUM_NAME, @SUBJECT = dbo.Q_FORUM_TOPIC_T.TOPIC_SUBJECT,
        @DISCO = DISCOGRAPHY
        FROM         dbo.Q_FORUM_T INNER JOIN
                              dbo.Q_FORUM_TOPIC_T ON dbo.Q_FORUM_T.Q_FORUM_ID = dbo.Q_FORUM_TOPIC_T.Q_FORUM_ID
        WHERE   dbo.Q_FORUM_TOPIC_T.Q_FORUM_TOPIC_ID = @Q_FORUM_TOPIC_ID
        SET NOCOUNT OFF
        """,
    ];

    public static async Task InstallAsync(DbContext schema)
    {
        foreach (var batch in InstallBatches)
        {
            await schema.Database.ExecuteSqlRawAsync(batch);
        }
    }
}
