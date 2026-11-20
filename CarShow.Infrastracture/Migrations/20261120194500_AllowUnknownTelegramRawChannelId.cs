using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261120194500_AllowUnknownTelegramRawChannelId")]
public sealed class AllowUnknownTelegramRawChannelId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Numeric Telegram peer IDs are not available from the public HTML feed.
        // Preserve existing IDs; allow new raw messages to have an unknown peer ID.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramRawMessages', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns c
                   JOIN sys.types t ON c.user_type_id = t.user_type_id
                   WHERE c.object_id = OBJECT_ID(N'dbo.TelegramRawMessages')
                     AND c.name = N'ChannelId'
                     AND t.name = N'bigint'
                     AND c.is_nullable = 0
               )
                ALTER TABLE dbo.TelegramRawMessages ALTER COLUMN ChannelId bigint NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive rollback: some archived rows may have unknown channel IDs.
    }
}
