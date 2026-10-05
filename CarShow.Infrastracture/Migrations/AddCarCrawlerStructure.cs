using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    public partial class AddCarCrawlerStructure : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Price",
                table: "Cars",
                newName: "MarketPrice");

            migrationBuilder.AlterColumn<decimal>(
                name: "MarketPrice",
                table: "Cars",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(float),
                oldType: "real");

            migrationBuilder.AddColumn<decimal>(
                name: "FactoryPrice",
                table: "Cars",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Cars",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Cars",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdated",
                table: "Cars",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AlterColumn<long?>(
                name: "TipId",
                table: "Cars",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Cars");

            migrationBuilder.CreateTable(
                name: "CarPriceHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDelete = table.Column<bool>(type: "bit", nullable: false),
                    CarId = table.Column<long>(type: "bigint", nullable: false),
                    MarketPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FactoryPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CarPriceHistories_Cars_CarId",
                        column: x => x.CarId,
                        principalTable: "Cars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarPriceHistories_CarId",
                table: "CarPriceHistories",
                column: "CarId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarPriceHistories");

            migrationBuilder.DropColumn(name: "FactoryPrice", table: "Cars");
            migrationBuilder.DropColumn(name: "Slug", table: "Cars");
            migrationBuilder.DropColumn(name: "SourceUrl", table: "Cars");
            migrationBuilder.DropColumn(name: "LastUpdated", table: "Cars");

            migrationBuilder.AddColumn<long>(
                name: "CategoryId",
                table: "Cars",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "StartDate",
                table: "Cars",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<long>(
                name: "TipId",
                table: "Cars",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long?),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "MarketPrice",
                table: "Cars",
                newName: "Price");

            migrationBuilder.AlterColumn<float>(
                name: "Price",
                table: "Cars",
                type: "real",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");
        }
    }
}
