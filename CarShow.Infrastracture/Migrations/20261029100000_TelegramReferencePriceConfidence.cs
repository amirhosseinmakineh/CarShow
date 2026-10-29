using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public sealed partial class TelegramReferencePriceConfidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("IndependentSourceCount", "CarPriceHistories", "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>("ConfidenceLevel", "CarPriceHistories", "nvarchar(max)", nullable: false, defaultValue: "Low");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("IndependentSourceCount", "CarPriceHistories");
        migrationBuilder.DropColumn("ConfidenceLevel", "CarPriceHistories");
    }
}
