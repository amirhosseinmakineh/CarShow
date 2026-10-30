using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class IndependentZeroVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZeroVehicleImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MessageLimit = table.Column<int>(type: "int", nullable: false),
                    Before = table.Column<long>(type: "bigint", nullable: true),
                    NextBefore = table.Column<long>(type: "bigint", nullable: true),
                    PagesFetched = table.Column<int>(type: "int", nullable: false),
                    StopReason = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Scanned = table.Column<int>(type: "int", nullable: false),
                    Accepted = table.Column<int>(type: "int", nullable: false),
                    Rejected = table.Column<int>(type: "int", nullable: false),
                    Duplicates = table.Column<int>(type: "int", nullable: false),
                    Stored = table.Column<int>(type: "int", nullable: false),
                    PriceGroups = table.Column<int>(type: "int", nullable: false),
                    RejectionCounts = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehicleModels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Aliases = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehicleTrims",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Aliases = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleTrims", x => x.Id);
                    table.UniqueConstraint("AK_ZeroVehicleTrims_Id_ModelId", x => new { x.Id, x.ModelId });
                    table.ForeignKey(
                        name: "FK_ZeroVehicleTrims_ZeroVehicleModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ZeroVehicleModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehicleListings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelId = table.Column<long>(type: "bigint", nullable: false),
                    TrimId = table.Column<long>(type: "bigint", nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PriceToman = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalMessageId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RawText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PricingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehicleListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZeroVehicleListings_ZeroVehicleModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ZeroVehicleModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZeroVehicleListings_ZeroVehicleTrims_TrimId_ModelId",
                        columns: x => new { x.TrimId, x.ModelId },
                        principalTable: "ZeroVehicleTrims",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZeroVehiclePriceGroups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModelId = table.Column<long>(type: "bigint", nullable: false),
                    TrimId = table.Column<long>(type: "bigint", nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    SourceCount = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZeroVehiclePriceGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZeroVehiclePriceGroups_ZeroVehicleModels_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ZeroVehicleModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ZeroVehiclePriceGroups_ZeroVehicleTrims_TrimId_ModelId",
                        columns: x => new { x.TrimId, x.ModelId },
                        principalTable: "ZeroVehicleTrims",
                        principalColumns: new[] { "Id", "ModelId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleImportRuns_Channel_Status",
                table: "ZeroVehicleImportRuns",
                columns: new[] { "Channel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleImportRuns_Status_CreatedAt",
                table: "ZeroVehicleImportRuns",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_Channel_ExternalMessageId",
                table: "ZeroVehicleListings",
                columns: new[] { "Channel", "ExternalMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_ModelId_TrimId_PublishedAt",
                table: "ZeroVehicleListings",
                columns: new[] { "ModelId", "TrimId", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_PublishedAt",
                table: "ZeroVehicleListings",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleListings_TrimId_ModelId",
                table: "ZeroVehicleListings",
                columns: new[] { "TrimId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleModels_Name",
                table: "ZeroVehicleModels",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehiclePriceGroups_ModelId_TrimId",
                table: "ZeroVehiclePriceGroups",
                columns: new[] { "ModelId", "TrimId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehiclePriceGroups_TrimId_ModelId",
                table: "ZeroVehiclePriceGroups",
                columns: new[] { "TrimId", "ModelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleTrims_ModelId_Name",
                table: "ZeroVehicleTrims",
                columns: new[] { "ModelId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZeroVehicleImportRuns");

            migrationBuilder.DropTable(
                name: "ZeroVehicleListings");

            migrationBuilder.DropTable(
                name: "ZeroVehiclePriceGroups");

            migrationBuilder.DropTable(
                name: "ZeroVehicleTrims");

            migrationBuilder.DropTable(
                name: "ZeroVehicleModels");
        }
    }
}
