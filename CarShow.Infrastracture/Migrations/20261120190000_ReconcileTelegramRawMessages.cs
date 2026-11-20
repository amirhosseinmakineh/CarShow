using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261120190000_ReconcileTelegramRawMessages")]
public sealed class ReconcileTelegramRawMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent additive migration; retains all pre-existing archive rows.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramRawMessages', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TelegramRawMessages (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TelegramMessageId bigint NOT NULL,
                    ChannelId nvarchar(128) NULL,
                    ChannelName nvarchar(256) NULL,
                    ChannelUsername nvarchar(128) NOT NULL,
                    SenderName nvarchar(256) NULL,
                    [Text] nvarchar(max) NOT NULL,
                    MessageUrl nvarchar(512) NOT NULL,
                    PublishedAt datetimeoffset NOT NULL,
                    CollectedAt datetimeoffset NOT NULL,
                    Metadata nvarchar(max) NULL
                );
            END;
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'Id') IS NULL
                THROW 51001, 'TelegramRawMessages lacks Id; manual schema review required to preserve existing rows.', 1;
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'TelegramMessageId') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD TelegramMessageId bigint NOT NULL CONSTRAINT DF_TRM_TelegramMessageId DEFAULT(0);
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'ChannelId') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD ChannelId nvarchar(128) NULL;
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'ChannelName') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD ChannelName nvarchar(256) NULL;
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'ChannelUsername') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD ChannelUsername nvarchar(128) NOT NULL CONSTRAINT DF_TRM_ChannelUsername DEFAULT(N'legacy');
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'SenderName') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD SenderName nvarchar(256) NULL;
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'Text') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD [Text] nvarchar(max) NOT NULL CONSTRAINT DF_TRM_Text DEFAULT(N'');
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'MessageUrl') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD MessageUrl nvarchar(512) NOT NULL CONSTRAINT DF_TRM_MessageUrl DEFAULT(N'');
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'PublishedAt') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD PublishedAt datetimeoffset NOT NULL CONSTRAINT DF_TRM_PublishedAt DEFAULT('2000-01-01T00:00:00+00:00');
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'CollectedAt') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD CollectedAt datetimeoffset NOT NULL CONSTRAINT DF_TRM_CollectedAt DEFAULT('2000-01-01T00:00:00+00:00');
            IF COL_LENGTH(N'dbo.TelegramRawMessages', N'Metadata') IS NULL
                ALTER TABLE dbo.TelegramRawMessages ADD Metadata nvarchar(max) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TelegramRawMessages') AND name = N'IX_TelegramRawMessages_PublishedAt')
                CREATE INDEX IX_TelegramRawMessages_PublishedAt ON dbo.TelegramRawMessages(PublishedAt DESC);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately non-destructive: older archive tables and data may predate this migration.
    }
}
