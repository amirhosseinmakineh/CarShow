using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public partial class SmartTelegramComparisons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'Manufacturer') IS NULL ALTER TABLE dbo.CarListings ADD Manufacturer nvarchar(128) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ProcessingStatus') IS NULL ALTER TABLE dbo.CarListings ADD ProcessingStatus nvarchar(32) NOT NULL CONSTRAINT DF_CarListings_ProcessingStatus DEFAULT N'Pending';
            IF COL_LENGTH('dbo.CarListings', 'ComparisonStatus') IS NULL ALTER TABLE dbo.CarListings ADD ComparisonStatus nvarchar(32) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ComparisonReason') IS NULL ALTER TABLE dbo.CarListings ADD ComparisonReason nvarchar(max) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ComparisonAveragePrice') IS NULL ALTER TABLE dbo.CarListings ADD ComparisonAveragePrice decimal(18,2) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ComparedChannelCount') IS NULL ALTER TABLE dbo.CarListings ADD ComparedChannelCount int NOT NULL CONSTRAINT DF_CarListings_ComparedChannelCount DEFAULT 0;
            IF COL_LENGTH('dbo.CarListings', 'ValidPriceSourceCount') IS NULL ALTER TABLE dbo.CarListings ADD ValidPriceSourceCount int NOT NULL CONSTRAINT DF_CarListings_ValidPriceSourceCount DEFAULT 0;
            IF COL_LENGTH('dbo.CarListings', 'PrimaryImageUrl') IS NULL ALTER TABLE dbo.CarListings ADD PrimaryImageUrl nvarchar(max) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ImageUrls') IS NULL ALTER TABLE dbo.CarListings ADD ImageUrls nvarchar(max) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CarListings_ProcessingStatus' AND object_id = OBJECT_ID('dbo.CarListings'))
                CREATE INDEX IX_CarListings_ProcessingStatus ON dbo.CarListings(ProcessingStatus);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CarListings_ComparisonStatus' AND object_id = OBJECT_ID('dbo.CarListings'))
                CREATE INDEX IX_CarListings_ComparisonStatus ON dbo.CarListings(ComparisonStatus);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately preserve columns and data on rollback. Roll back the application only;
        // this additive schema is safe to leave in place and can be reused on a forward deploy.
    }
}
