using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public partial class OptionalTelegramManufacturer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_Cars_Companies_CompanyId", "Cars");
        migrationBuilder.DropIndex("IX_Cars_CompanyId", "Cars");
        migrationBuilder.AlterColumn<long>(
            name: "CompanyId", table: "Cars", type: "bigint",
            nullable: true, oldClrType: typeof(long), oldType: "bigint");
        migrationBuilder.CreateIndex("IX_Cars_CompanyId", "Cars", "CompanyId");
        migrationBuilder.AddForeignKey("FK_Cars_Companies_CompanyId", "Cars", "CompanyId",
            "Companies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddColumn<string>("RawCarName", "CarListings", "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>("ExtractionStatus", "CarListings", "nvarchar(32)", maxLength: 32,
            nullable: false, defaultValue: "Extracted");
        migrationBuilder.AddColumn<string>("NameValidationStatus", "CarListings", "nvarchar(32)", maxLength: 32,
            nullable: false, defaultValue: "NeedsReview");
        migrationBuilder.AddColumn<string>("PriceValidationStatus", "CarListings", "nvarchar(32)", maxLength: 32,
            nullable: false, defaultValue: "NeedsReview");
        migrationBuilder.Sql("UPDATE [CarListings] SET [NameValidationStatus] = 'Validated' WHERE [Model] IS NOT NULL AND LTRIM(RTRIM([Model])) <> '';");
        migrationBuilder.Sql("UPDATE [CarListings] SET [PriceValidationStatus] = 'Validated' WHERE [NormalizedPrice] > 0 AND [Currency] IN ('IRT', 'IRR');");
        migrationBuilder.Sql("UPDATE [CarListings] SET [ExtractionStatus] = 'Validated' WHERE [NameValidationStatus] = 'Validated' AND [PriceValidationStatus] = 'Validated';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // This downgrade is intentionally refused if any manufacturer-less cars exist.
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Cars] WHERE [CompanyId] IS NULL) THROW 51000, 'Cannot restore required CompanyId while Cars contain null values.', 1;");
        migrationBuilder.DropColumn("RawCarName", "CarListings");
        migrationBuilder.DropColumn("ExtractionStatus", "CarListings");
        migrationBuilder.DropColumn("NameValidationStatus", "CarListings");
        migrationBuilder.DropColumn("PriceValidationStatus", "CarListings");
        migrationBuilder.DropForeignKey("FK_Cars_Companies_CompanyId", "Cars");
        migrationBuilder.DropIndex("IX_Cars_CompanyId", "Cars");
        migrationBuilder.AlterColumn<long>(
            name: "CompanyId", table: "Cars", type: "bigint",
            nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
        migrationBuilder.CreateIndex("IX_Cars_CompanyId", "Cars", "CompanyId");
        migrationBuilder.AddForeignKey("FK_Cars_Companies_CompanyId", "Cars", "CompanyId",
            "Companies", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
}
