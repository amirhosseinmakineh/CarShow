using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261121060000_AddCarReferenceApproval")]
public sealed class AddCarReferenceApproval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsReferenceApproved", table: "Cars", type: "bit",
            nullable: false, defaultValue: false);
        // Backfill only records explicitly approved by the last stored comparison.
        migrationBuilder.Sql("""
            UPDATE dbo.Cars
            SET IsReferenceApproved = 1
            WHERE IsDelete = 0
              AND ISJSON([Description]) = 1
              AND JSON_VALUE([Description], '$.Type') = 'TelegramPriceReference'
              AND JSON_VALUE([Description], '$.ComparisonApproved') = 'true'
              AND JSON_VALUE([Description], '$.PriceStatus') = 'Approved'
            """);
        migrationBuilder.CreateIndex(
            name: "IX_Cars_IsReferenceApproved",
            table: "Cars", column: "IsReferenceApproved");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Cars_IsReferenceApproved", table: "Cars");
        migrationBuilder.DropColumn(name: "IsReferenceApproved", table: "Cars");
    }
}
