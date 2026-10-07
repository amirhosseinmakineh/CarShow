using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

[Migration("20261007200000_AddCrawlerColumns")]
public partial class AddCrawlerColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'SourceUrl') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [SourceUrl] nvarchar(max) NULL;
            IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [FactoryPrice] decimal(18,2) NOT NULL CONSTRAINT [DF_Cars_FactoryPrice] DEFAULT (0);
            IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [MarketPrice] decimal(18,2) NOT NULL CONSTRAINT [DF_Cars_MarketPrice] DEFAULT (0);
            IF COL_LENGTH('dbo.Cars', 'Slug') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [Slug] nvarchar(450) NOT NULL CONSTRAINT [DF_Cars_Slug] DEFAULT ('');
            IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [LastUpdated] datetime2 NOT NULL CONSTRAINT [DF_Cars_LastUpdated] DEFAULT (GETUTCDATE());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP CONSTRAINT IF EXISTS [DF_Cars_LastUpdated];
            IF COL_LENGTH('dbo.Cars', 'Slug') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP CONSTRAINT IF EXISTS [DF_Cars_Slug];
            IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP CONSTRAINT IF EXISTS [DF_Cars_MarketPrice];
            IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP CONSTRAINT IF EXISTS [DF_Cars_FactoryPrice];
            IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP COLUMN [LastUpdated];
            IF COL_LENGTH('dbo.Cars', 'Slug') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP COLUMN [Slug];
            IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP COLUMN [MarketPrice];
            IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP COLUMN [FactoryPrice];
            IF COL_LENGTH('dbo.Cars', 'SourceUrl') IS NOT NULL ALTER TABLE [dbo].[Cars] DROP COLUMN [SourceUrl];
            """);
    }
}
