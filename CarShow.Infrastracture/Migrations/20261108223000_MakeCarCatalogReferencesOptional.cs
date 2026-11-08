using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261108223000_MakeCarCatalogReferencesOptional")]
public class MakeCarCatalogReferencesOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Cars_CarModels_CarModelId", "Cars");
        migrationBuilder.DropForeignKey("FK_Cars_Companies_CompanyId", "Cars");
        migrationBuilder.DropForeignKey("FK_Cars_Tips_TipId", "Cars");

        foreach (var column in new[] { "CarModelId", "CompanyId", "TipId" })
            migrationBuilder.AlterColumn<long>(column, "Cars", type: "bigint", nullable: true,
                oldClrType: typeof(long), oldType: "bigint");

        migrationBuilder.AddForeignKey("FK_Cars_CarModels_CarModelId", "Cars", "CarModelId",
            "CarModels", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_Cars_Companies_CompanyId", "Cars", "CompanyId",
            "Companies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_Cars_Tips_TipId", "Cars", "TipId",
            "Tips", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Refuse rollback when optional references exist; never invent IDs or delete rows.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [Cars] WHERE [CarModelId] IS NULL OR [CompanyId] IS NULL OR [TipId] IS NULL)
                THROW 51000, 'Cannot make car references required while NULL references exist.', 1;
            """);

        migrationBuilder.DropForeignKey("FK_Cars_CarModels_CarModelId", "Cars");
        migrationBuilder.DropForeignKey("FK_Cars_Companies_CompanyId", "Cars");
        migrationBuilder.DropForeignKey("FK_Cars_Tips_TipId", "Cars");

        foreach (var column in new[] { "CarModelId", "CompanyId", "TipId" })
            migrationBuilder.AlterColumn<long>(column, "Cars", type: "bigint", nullable: false,
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);

        migrationBuilder.AddForeignKey("FK_Cars_CarModels_CarModelId", "Cars", "CarModelId",
            "CarModels", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_Cars_Companies_CompanyId", "Cars", "CompanyId",
            "Companies", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        migrationBuilder.AddForeignKey("FK_Cars_Tips_TipId", "Cars", "TipId",
            "Tips", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
    }
}
