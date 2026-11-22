using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CarShow.Infrastracture.Migrations;

[DbContext(typeof(CarShowContext))]
[Migration("20261122090000_AddCarModelImage")]
public sealed class AddCarModelImage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ImageName", table: "CarModels", type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ImageName", table: "CarModels");
    }
}
