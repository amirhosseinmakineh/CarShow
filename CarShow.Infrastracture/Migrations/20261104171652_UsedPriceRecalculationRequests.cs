using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class UsedPriceRecalculationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PricingRequestCompletedAt",
                table: "TelegramPricingStates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PricingRequestedAt",
                table: "TelegramPricingStates",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PricingRequestCompletedAt",
                table: "TelegramPricingStates");

            migrationBuilder.DropColumn(
                name: "PricingRequestedAt",
                table: "TelegramPricingStates");
        }
    }
}
