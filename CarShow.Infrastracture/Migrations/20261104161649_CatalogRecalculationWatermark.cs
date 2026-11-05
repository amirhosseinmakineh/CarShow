using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class CatalogRecalculationWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecalculatedAt",
                table: "ZeroVehicleSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecalculationRequestedAt",
                table: "ZeroVehicleSettings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRecalculatedAt",
                table: "ZeroVehicleSettings");

            migrationBuilder.DropColumn(
                name: "RecalculationRequestedAt",
                table: "ZeroVehicleSettings");
        }
    }
}
