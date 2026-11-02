using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class ZeroVehicleCarIrPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZeroVehicleListings_Channel_ExternalMessageId",
                table: "ZeroVehicleListings");

            migrationBuilder.AddColumn<decimal>(
                name: "MarketPrice",
                table: "ZeroVehiclePriceGroups",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PricingStatus",
                table: "ZeroVehiclePriceGroups",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unavailable");

            migrationBuilder.AddColumn<decimal>(
                name: "FinalPrice",
                table: "ZeroVehicleListings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketFetchedAt",
                table: "ZeroVehicleListings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketName",
                table: "ZeroVehicleListings",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketPrice",
                table: "ZeroVehicleListings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketPriceAt",
                table: "ZeroVehicleListings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketStatus",
                table: "ZeroVehicleListings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarketUrl",
                table: "ZeroVehicleListings",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartIndex",
                table: "ZeroVehicleListings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PriceCandidates",
                table: "ZeroVehicleListings",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawPrice",
                table: "ZeroVehicleListings",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ZeroVehicleListings",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Extracted",
                table: "ZeroVehicleImportRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ZeroVehicleChannels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Identifier = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    ScanBefore = table.Column<long>(type: "bigint", nullable: true),
                    PendingLatestMessageId = table.Column<long>(type: "bigint", nullable: true),
                    LatestMessageId = table.Column<long>(type: "bigint", nullable: true),
                    LastImportedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehicleIssues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PartIndex = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZeroVehicleIssues_ZeroVehicleImportRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ZeroVehicleImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehicleSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviationPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_Channel_ExternalMessageId_PartIndex",
                table: "ZeroVehicleListings",
                columns: new[] { "Channel", "ExternalMessageId", "PartIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleChannels_Identifier",
                table: "ZeroVehicleChannels",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleIssues_RunId_ExternalMessageId_PartIndex",
                table: "ZeroVehicleIssues",
                columns: new[] { "RunId", "ExternalMessageId", "PartIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZeroVehicleChannels");

            migrationBuilder.DropTable(
                name: "ZeroVehicleIssues");

            migrationBuilder.DropTable(
                name: "ZeroVehicleSettings");

            migrationBuilder.DropIndex(
                name: "IX_ZeroVehicleListings_Channel_ExternalMessageId_PartIndex",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketPrice",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "PricingStatus",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "FinalPrice",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketFetchedAt",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketName",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketPrice",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketPriceAt",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketStatus",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "MarketUrl",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "PartIndex",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "PriceCandidates",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "RawPrice",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "Extracted",
                table: "ZeroVehicleImportRuns");

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_Channel_ExternalMessageId",
                table: "ZeroVehicleListings",
                columns: new[] { "Channel", "ExternalMessageId" },
                unique: true);
        }
    }
}
