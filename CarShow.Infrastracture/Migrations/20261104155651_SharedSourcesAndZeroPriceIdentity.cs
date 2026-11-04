using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations
{
    /// <inheritdoc />
    public partial class SharedSourcesAndZeroPriceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZeroVehiclePriceGroups_ModelId_TrimId",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.AddColumn<string>(
                name: "ConfigurationKey",
                table: "ZeroVehicleTrims",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transmission",
                table: "ZeroVehicleTrims",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedMarketPrice",
                table: "ZeroVehiclePriceGroups",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualOverridePrice",
                table: "ZeroVehiclePriceGroups",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualOverrideUntil",
                table: "ZeroVehiclePriceGroups",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriceDay",
                table: "ZeroVehiclePriceGroups",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceReviewStatus",
                table: "ZeroVehiclePriceGroups",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "ZeroVehiclePriceGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YearCalendar",
                table: "ZeroVehiclePriceGroups",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompanyId",
                table: "ZeroVehicleModels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfigurationKey",
                table: "ZeroVehicleModels",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminDecision",
                table: "ZeroVehicleListings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyMatched",
                table: "ZeroVehicleListings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PriceDay",
                table: "ZeroVehicleListings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YearCalendar",
                table: "ZeroVehicleListings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LatestMessageId",
                table: "TelegramChannels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PendingLatestMessageId",
                table: "TelegramChannels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanBefore",
                table: "TelegramChannels",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CalculatedMarketPrice",
                table: "Cars",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualOverridePrice",
                table: "Cars",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualOverrideUntil",
                table: "Cars",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceReviewStatus",
                table: "Cars",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AdminDecision",
                table: "CarListings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyMatched",
                table: "CarListings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MarketPriceReview",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceReview", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehiclePriceGroups_ModelId_TrimId_Year_YearCalendar_PriceDay",
                table: "ZeroVehiclePriceGroups",
                columns: new[] { "ModelId", "TrimId", "Year", "YearCalendar", "PriceDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehicleModels_CompanyId",
                table: "ZeroVehicleModels",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceReview_Scope_TargetId_CreatedAt",
                table: "MarketPriceReview",
                columns: new[] { "Scope", "TargetId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ZeroVehicleModels_Companies_CompanyId",
                table: "ZeroVehicleModels",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    UPDATE ZeroVehicleListings SET YearCalendar = CASE WHEN Year BETWEEN 1300 AND 1500 THEN N'Jalali' WHEN Year BETWEEN 1900 AND 2100 THEN N'Gregorian' ELSE NULL END,
                     PriceDay = CONVERT(date, DATEADD(minute, 210, PublishedAt)), AdminDecision = N'Unreviewed';
                    UPDATE ZeroVehiclePriceGroups SET CalculatedMarketPrice = ReferencePrice, PricingStatus = N'LegacyNeedsReview', PriceReviewStatus = N'Unreviewed';
                    UPDATE Cars SET CalculatedMarketPrice = MarketPrice, PriceReviewStatus = N'Unreviewed';
                    UPDATE CarListings SET AdminDecision = N'Unreviewed';
                    UPDATE ZeroVehicleModels SET ConfigurationKey = Name;
                    UPDATE ZeroVehicleTrims SET ConfigurationKey = Name;
                    """);
                migrationBuilder.Sql("""
                    -- Owner-approved source registry, 2026-11-04. Idempotent; no deletes.
                    DECLARE @Approved TABLE (Identifier nvarchar(256) NOT NULL PRIMARY KEY);
                    INSERT INTO @Approved (Identifier) VALUES
                    (N'autopalermo'),
                    (N'davoodkelasii'),
                    (N'tajcarsefr'),
                    (N'autozamanzade'),
                    (N'miladforoud'),
                    (N'autoland41'),
                    (N'haminkhodroo'),
                    (N'adib_khodro_qom'),
                    (N'gallery20moradi'),
                    (N'car_sales_gandomi'),
                    (N'autosolhi'),
                    (N'behzadsaberi_oxinkhodro'),
                    (N'auto_ap'),
                    (N'avatttgalry'),
                    (N'tehrankhodro97'),
                    (N'mohamadrezamansoor'),
                    (N'payamkhodrooo'),
                    (N'auto_teymouri'),
                    (N'shahabbesharaty'),
                    (N'autoesmieh'),
                    (N'omidabbasicarsale'),
                    (N'otoeiliya'),
                    (N'autogalerymhmood'),
                    (N'kharid0forosh0mashin'),
                    (N'eshraghicar'),
                    (N'irankodroohadiali'),
                    (N'namayeshgahdarantehran21'),
                    (N'hashemi_car2'),
                    (N'bazarkhodroh'),
                    (N'modern_khodrooooo'),
                    (N'thehhrankhodrow'),
                    (N'autoplack'),
                    (N'automarkethamidnasiri'),
                    (N'goroohmashin'),
                    (N'khodro_mashin_tehran_divar_bama'),
                    (N'otosefr_farzadakbari'),
                    (N'rafieicar'),
                    (N'hhirsa'),
                    (N'wolfkhodro'),
                    (N'sefidgarali'),
                    (N'aradkhodro96'),
                    (N'payamrahnama'),
                    (N'uotobehroz'),
                    (N'autoluxy'),
                    (N'otoadelkhani'),
                    (N'autogorgangolbini'),
                    (N'ehsaaant'),
                    (N'autoramin9787'),
                    (N'automeysamsaghi'),
                    (N'auto_dayy'),
                    (N'shakiba_iran_16'),
                    (N'hoseinbabaieqom'),
                    (N'auto_royal_meshkin'),
                    (N'auto_najafiqom'),
                    (N'mehrdadloghmanii'),
                    (N'alavicarexhibition'),
                    (N'mamatir_khodro_hosseini'),
                    (N'grohcobrotehran'),
                    (N'khazarrkhodro'),
                    (N'auto_1abdolmaleki1'),
                    (N'autopasdaran'),
                    (N'hakimghailarrr'),
                    (N'auto_yektaa'),
                    (N'jamshidsamadiiii'),
                    (N'auto_jahan'),
                    (N'ilam98khodro'),
                    (N'autosardarr'),
                    (N'khodro_nasri'),
                    (N'+kF0Dpp9cMl45MjNk'),
                    (N'autovalayati'),
                    (N'outorashidiyan'),
                    (N'sahracar'),
                    (N'arasmehri'),
                    (N'parsiankhodroqolizade'),
                    (N'kiu2016'),
                    (N'arsinkhodro'),
                    (N'bakshabadi'),
                    (N'automojtaba66'),
                    (N'autoeightasgarinia'),
                    (N'keyvancar'),
                    (N'pejman2616'),
                    (N'auto_tabrizi'),
                    (N'hesamfatemikaraj'),
                    (N'autofezolahi'),
                    (N'irankhodromonirikashan');
                    -- Reuse historical source IDs when URL scheme, case or trailing slash differs.
                    -- Duplicate historical rows remain in place and are paused; their messages are not moved or deleted.
                    DECLARE @Candidates TABLE (SourceId bigint NOT NULL, Identifier nvarchar(256) NOT NULL);
                    INSERT INTO @Candidates (SourceId, Identifier)
                    SELECT c.Id, CASE
                     WHEN LEFT(LTRIM(RTRIM(v.Value)),1) = '+' THEN LTRIM(RTRIM(v.Value))
                     WHEN CHARINDEX('/+', LTRIM(RTRIM(v.Value))) > 0 THEN SUBSTRING(LTRIM(RTRIM(v.Value)), CHARINDEX('/+', LTRIM(RTRIM(v.Value))) + 1, 256)
                     ELSE REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LOWER(LTRIM(RTRIM(v.Value))), N'https://www.t.me/', N''), N'http://www.t.me/', N''), N'https://t.me/', N''), N'http://t.me/', N''), N'https://telegram.me/', N''), N'http://telegram.me/', N'') END
                    FROM TelegramChannels c CROSS APPLY (VALUES(c.NormalizedIdentifier),(c.TelegramIdentifier),(c.Username),(c.PublicUrl)) v(Value)
                    WHERE v.Value IS NOT NULL AND LEN(LTRIM(RTRIM(v.Value))) > 0 AND LEN(v.Value) <= 256;
                    WHILE EXISTS (SELECT 1 FROM @Candidates WHERE RIGHT(Identifier,1) = '/')
                     UPDATE @Candidates SET Identifier = LEFT(Identifier,LEN(Identifier)-1) WHERE RIGHT(Identifier,1) = '/';
                    UPDATE @Candidates SET Identifier = SUBSTRING(Identifier,2,256) WHERE LEFT(Identifier,1) = '@';
                    UPDATE @Candidates SET Identifier = SUBSTRING(Identifier,3,256) WHERE LEFT(Identifier,2) = 's/';
                    DECLARE @Matches TABLE (SourceId bigint NOT NULL, Identifier nvarchar(256) NOT NULL);
                    INSERT INTO @Matches SELECT DISTINCT c.SourceId, a.Identifier FROM @Candidates c JOIN @Approved a
                     ON c.Identifier COLLATE Latin1_General_100_BIN2 = a.Identifier COLLATE Latin1_General_100_BIN2;
                    DECLARE @ConflictingSource bigint = (SELECT TOP(1) SourceId FROM @Matches GROUP BY SourceId HAVING COUNT(*) > 1 ORDER BY SourceId);
                    IF @ConflictingSource IS NOT NULL
                    BEGIN
                     DECLARE @SourceConflictMessage nvarchar(2048) = N'Conflicting source metadata. Review source ID ' + CONVERT(nvarchar(32), @ConflictingSource) + N' before retrying this migration.';
                     THROW 51001, @SourceConflictMessage, 1;
                    END;
                    DECLARE @PrimarySources TABLE (Identifier nvarchar(256) NOT NULL PRIMARY KEY, SourceId bigint NOT NULL);
                    INSERT INTO @PrimarySources
                    SELECT m.Identifier, COALESCE(MIN(CASE WHEN c.NormalizedIdentifier COLLATE Latin1_General_100_BIN2 = m.Identifier COLLATE Latin1_General_100_BIN2 THEN c.Id END), MIN(c.Id))
                    FROM @Matches m JOIN TelegramChannels c ON c.Id = m.SourceId GROUP BY m.Identifier;
                    UPDATE c SET Enabled = 0, Status = N'Paused', LastError = N'DuplicateSourceAlias: use canonical source ' + CONVERT(nvarchar(32),p.SourceId), UpdatedAt = SYSUTCDATETIME()
                    FROM TelegramChannels c JOIN @Matches m ON m.SourceId = c.Id JOIN @PrimarySources p ON p.Identifier = m.Identifier WHERE c.Id <> p.SourceId;
                    UPDATE c SET NormalizedIdentifier = p.Identifier, TelegramIdentifier = p.Identifier,
                     Username = CASE WHEN LEFT(p.Identifier,1) = '+' THEN N'' ELSE p.Identifier END, IsDelete = 0, UpdatedAt = SYSUTCDATETIME()
                    FROM TelegramChannels c JOIN @PrimarySources p ON p.SourceId = c.Id;

                    UPDATE c SET Enabled = 0, Status = N'Paused', UpdatedAt = SYSUTCDATETIME()
                    FROM TelegramChannels c WHERE NOT EXISTS (SELECT 1 FROM @Approved a WHERE a.Identifier = c.NormalizedIdentifier);
                    INSERT INTO TelegramChannels
                    (IsDelete, TelegramIdentifier, NormalizedIdentifier, Username, PublicUrl, Title, Status, Enabled, CollectorType,
                     MessagesCollected, ListingsExtracted, RetryCount, CreatedAt, UpdatedAt, LastError)
                    SELECT 0, a.Identifier, a.Identifier, CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'' ELSE a.Identifier END,
                     N'https://t.me/' + a.Identifier, N'', CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'AccessRequired' ELSE N'Pending' END,
                     1, N'telegram', 0, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(),
                     CASE WHEN LEFT(a.Identifier,1) = '+' THEN N'Private invite: public preview access is unavailable.' ELSE NULL END
                    FROM @Approved a WHERE NOT EXISTS (SELECT 1 FROM TelegramChannels c WHERE c.NormalizedIdentifier = a.Identifier);
                    -- Existing approved sources retain their manager-selected enabled/deleted state.
                    -- ZeroVehicleChannels remains a checkpoint table only; it is not a source authority.
                    UPDATE c SET IsDelete = 0, UpdatedAt = SYSUTCDATETIME() FROM TelegramChannels c
                    WHERE c.IsDelete = 1 AND EXISTS (SELECT 1 FROM @Approved a WHERE a.Identifier = c.NormalizedIdentifier);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ZeroVehicleModels_Companies_CompanyId",
                table: "ZeroVehicleModels");

            migrationBuilder.DropTable(
                name: "MarketPriceReview");

            migrationBuilder.DropIndex(
                name: "IX_ZeroVehiclePriceGroups_ModelId_TrimId_Year_YearCalendar_PriceDay",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropIndex(
                name: "IX_ZeroVehicleModels_CompanyId",
                table: "ZeroVehicleModels");

            migrationBuilder.DropColumn(
                name: "ConfigurationKey",
                table: "ZeroVehicleTrims");

            migrationBuilder.DropColumn(
                name: "Transmission",
                table: "ZeroVehicleTrims");

            migrationBuilder.DropColumn(
                name: "CalculatedMarketPrice",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "ManualOverridePrice",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "ManualOverrideUntil",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "PriceDay",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "PriceReviewStatus",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "YearCalendar",
                table: "ZeroVehiclePriceGroups");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ZeroVehicleModels");

            migrationBuilder.DropColumn(
                name: "ConfigurationKey",
                table: "ZeroVehicleModels");

            migrationBuilder.DropColumn(
                name: "AdminDecision",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "ManuallyMatched",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "PriceDay",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "YearCalendar",
                table: "ZeroVehicleListings");

            migrationBuilder.DropColumn(
                name: "LatestMessageId",
                table: "TelegramChannels");

            migrationBuilder.DropColumn(
                name: "PendingLatestMessageId",
                table: "TelegramChannels");

            migrationBuilder.DropColumn(
                name: "ScanBefore",
                table: "TelegramChannels");

            migrationBuilder.DropColumn(
                name: "CalculatedMarketPrice",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "ManualOverridePrice",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "ManualOverrideUntil",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "PriceReviewStatus",
                table: "Cars");

            migrationBuilder.DropColumn(
                name: "AdminDecision",
                table: "CarListings");

            migrationBuilder.DropColumn(
                name: "ManuallyMatched",
                table: "CarListings");

            migrationBuilder.CreateIndex(
                name: "IX_ZeroVehiclePriceGroups_ModelId_TrimId",
                table: "ZeroVehiclePriceGroups",
                columns: new[] { "ModelId", "TrimId" },
                unique: true);
        }
    }
}
