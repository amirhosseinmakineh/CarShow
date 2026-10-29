using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public sealed partial class TelegramPipelineRunTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("RunStartedAt", "TelegramPricingStates", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RunCollectionCompletedAt", "TelegramPricingStates", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("RunCompletedAt", "TelegramPricingStates", "datetime2", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve recorded run timestamps if an older application is restored.
    }
}
