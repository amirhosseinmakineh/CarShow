using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class PricingExecutionReportsAndIssueProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "ZeroVehicleIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "ZeroVehicleIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResolvedListingId",
                table: "ZeroVehicleIssues",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "ZeroVehicleIssues",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedListings",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CalculatedPriceGroups",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExtractedListings",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NewRawMessages",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PricingCompletedAt",
                table: "TelegramCollectionJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedListings",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasons",
                table: "TelegramCollectionJobs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportVersion",
                table: "TelegramCollectionJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StageErrors",
                table: "TelegramCollectionJobs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "ZeroVehicleIssues");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "ZeroVehicleIssues");

            migrationBuilder.DropColumn(
                name: "ResolvedListingId",
                table: "ZeroVehicleIssues");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "ZeroVehicleIssues");

            migrationBuilder.DropColumn(
                name: "AcceptedListings",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "CalculatedPriceGroups",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "ExtractedListings",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "NewRawMessages",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "PricingCompletedAt",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "RejectedListings",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "RejectionReasons",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "ReportVersion",
                table: "TelegramCollectionJobs");

            migrationBuilder.DropColumn(
                name: "StageErrors",
                table: "TelegramCollectionJobs");
        }
    }
}
