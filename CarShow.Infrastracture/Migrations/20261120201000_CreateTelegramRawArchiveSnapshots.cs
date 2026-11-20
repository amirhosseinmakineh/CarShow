using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261120201000_CreateTelegramRawArchiveSnapshots")]
public sealed class CreateTelegramRawArchiveSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramRawArchiveSnapshots', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TelegramRawArchiveSnapshots (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TelegramMessageId bigint NOT NULL,
                    ChannelId bigint NULL,
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
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve archived messages on rollback.
    }
}
