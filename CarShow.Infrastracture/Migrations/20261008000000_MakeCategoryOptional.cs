using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

[Migration("20261008000000_MakeCategoryOptional")]
public partial class MakeCategoryOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'CategoryId') IS NOT NULL
            BEGIN
                ALTER TABLE [dbo].[Cars] ALTER COLUMN [CategoryId] bigint NULL;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'CategoryId') IS NOT NULL
            BEGIN
                UPDATE [dbo].[Cars] SET [CategoryId] = 0 WHERE [CategoryId] IS NULL;
                ALTER TABLE [dbo].[Cars] ALTER COLUMN [CategoryId] bigint NOT NULL;
            END
            """);
    }
}
