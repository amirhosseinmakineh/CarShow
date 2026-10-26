using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public partial class TelegramStrictComparisons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'Year') IS NULL ALTER TABLE dbo.Cars ADD Year int NULL;
            IF COL_LENGTH('dbo.Cars', 'YearCalendar') IS NULL ALTER TABLE dbo.Cars ADD YearCalendar nvarchar(16) NULL;
            IF COL_LENGTH('dbo.Cars', 'TelegramIdentityKey') IS NULL ALTER TABLE dbo.Cars ADD TelegramIdentityKey nvarchar(256) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ReasonCode') IS NULL ALTER TABLE dbo.CarListings ADD ReasonCode nvarchar(64) NULL;
            IF COL_LENGTH('dbo.CarListings', 'ValidatedAt') IS NULL ALTER TABLE dbo.CarListings ADD ValidatedAt datetime2 NULL;
            IF COL_LENGTH('dbo.CarListings', 'ComparedAt') IS NULL ALTER TABLE dbo.CarListings ADD ComparedAt datetime2 NULL;
            IF COL_LENGTH('dbo.CarListings', 'StoredAt') IS NULL ALTER TABLE dbo.CarListings ADD StoredAt datetime2 NULL;
            IF COL_LENGTH('dbo.CarListings', 'ComparisonKey') IS NULL ALTER TABLE dbo.CarListings ADD ComparisonKey nvarchar(256) NULL;
            IF COL_LENGTH('dbo.CarListings', 'YearCalendar') IS NULL ALTER TABLE dbo.CarListings ADD YearCalendar nvarchar(16) NULL;
            IF COL_LENGTH('dbo.CarListings', 'PriceText') IS NULL ALTER TABLE dbo.CarListings ADD PriceText nvarchar(64) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cars_TelegramIdentityKey' AND object_id = OBJECT_ID('dbo.Cars'))
                CREATE UNIQUE INDEX IX_Cars_TelegramIdentityKey ON dbo.Cars(TelegramIdentityKey) WHERE TelegramIdentityKey IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CarListings_ComparisonKey' AND object_id = OBJECT_ID('dbo.CarListings'))
                CREATE INDEX IX_CarListings_ComparisonKey ON dbo.CarListings(ComparisonKey);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only schema. Application rollback is safe; retaining nullable additive fields
        // prevents accidental loss of vehicle identity and processing audit data.
    }
}
