using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261126134000_AddCatalog")]
public class AddCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Catalog",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CompanyName = table.Column<string>(maxLength: 250, nullable: false),
                CarName = table.Column<string>(maxLength: 250, nullable: false),
                TrimName = table.Column<string>(maxLength: 250, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Catalog", x => x.Id));
        migrationBuilder.CreateIndex("IX_Catalog_CompanyName_CarName_TrimName",
            "Catalog", new[] { "CompanyName", "CarName", "TrimName" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Catalog");
    }
}
