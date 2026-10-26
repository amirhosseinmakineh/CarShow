using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public partial class SmartTelegramComparisons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Manufacturer", "CarListings", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("ProcessingStatus", "CarListings", maxLength: 32, nullable: false, defaultValue: "Pending");
        migrationBuilder.AddColumn<string>("ComparisonStatus", "CarListings", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>("ComparisonReason", "CarListings", nullable: true);
        migrationBuilder.AddColumn<decimal>("ComparisonAveragePrice", "CarListings", type: "decimal(18,2)", nullable: true);
        migrationBuilder.AddColumn<int>("ComparedChannelCount", "CarListings", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("ValidPriceSourceCount", "CarListings", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("PrimaryImageUrl", "CarListings", nullable: true);
        migrationBuilder.AddColumn<string>("ImageUrls", "CarListings", nullable: true);
        migrationBuilder.CreateIndex("IX_CarListings_ProcessingStatus", "CarListings", "ProcessingStatus");
        migrationBuilder.CreateIndex("IX_CarListings_ComparisonStatus", "CarListings", "ComparisonStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_CarListings_ProcessingStatus", "CarListings");
        migrationBuilder.DropIndex("IX_CarListings_ComparisonStatus", "CarListings");
        foreach (var column in new[] { "Manufacturer", "ProcessingStatus", "ComparisonStatus", "ComparisonReason", "ComparisonAveragePrice", "ComparedChannelCount", "ValidPriceSourceCount", "PrimaryImageUrl", "ImageUrls" })
            migrationBuilder.DropColumn(column, "CarListings");
    }
}
