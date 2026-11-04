BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    DROP INDEX [IX_ZeroVehiclePriceGroups_ModelId_TrimId] ON [ZeroVehiclePriceGroups];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleTrims] ADD [ConfigurationKey] nvarchar(128) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleTrims] ADD [Transmission] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [CalculatedMarketPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [ManualOverridePrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [ManualOverrideUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [PriceDay] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [PriceReviewStatus] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [Year] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [YearCalendar] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleModels] ADD [CompanyId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleModels] ADD [ConfigurationKey] nvarchar(128) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [AdminDecision] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [ManuallyMatched] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [PriceDay] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [YearCalendar] nvarchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [TelegramChannels] ADD [LatestMessageId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [TelegramChannels] ADD [PendingLatestMessageId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [TelegramChannels] ADD [ScanBefore] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [Cars] ADD [CalculatedMarketPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [Cars] ADD [ManualOverridePrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [Cars] ADD [ManualOverrideUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [Cars] ADD [PriceReviewStatus] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [CarListings] ADD [AdminDecision] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [CarListings] ADD [ManuallyMatched] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    CREATE TABLE [MarketPriceReview] (
        [Id] bigint NOT NULL IDENTITY,
        [Scope] nvarchar(32) NOT NULL,
        [TargetId] bigint NOT NULL,
        [Action] nvarchar(32) NOT NULL,
        [Actor] nvarchar(256) NOT NULL,
        [Reason] nvarchar(1024) NOT NULL,
        [BeforeJson] nvarchar(max) NOT NULL,
        [AfterJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_MarketPriceReview] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ZeroVehiclePriceGroups_ModelId_TrimId_Year_YearCalendar_PriceDay] ON [ZeroVehiclePriceGroups] ([ModelId], [TrimId], [Year], [YearCalendar], [PriceDay]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    CREATE INDEX [IX_ZeroVehicleModels_CompanyId] ON [ZeroVehicleModels] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    CREATE INDEX [IX_MarketPriceReview_Scope_TargetId_CreatedAt] ON [MarketPriceReview] ([Scope], [TargetId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    ALTER TABLE [ZeroVehicleModels] ADD CONSTRAINT [FK_ZeroVehicleModels_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [Companies] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    UPDATE ZeroVehicleListings SET YearCalendar = CASE WHEN Year BETWEEN 1300 AND 1500 THEN N'Jalali' WHEN Year BETWEEN 1900 AND 2100 THEN N'Gregorian' ELSE NULL END,
     PriceDay = CONVERT(date, DATEADD(minute, 210, PublishedAt)), AdminDecision = N'Unreviewed';
    UPDATE ZeroVehiclePriceGroups SET CalculatedMarketPrice = ReferencePrice, PricingStatus = N'LegacyNeedsReview', PriceReviewStatus = N'Unreviewed';
    UPDATE Cars SET CalculatedMarketPrice = MarketPrice, PriceReviewStatus = N'Unreviewed';
    UPDATE CarListings SET AdminDecision = N'Unreviewed';
    UPDATE ZeroVehicleModels SET ConfigurationKey = Name;
    UPDATE ZeroVehicleTrims SET ConfigurationKey = Name;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104155651_SharedSourcesAndZeroPriceIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104155651_SharedSourcesAndZeroPriceIdentity', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104161649_CatalogRecalculationWatermark'
)
BEGIN
    ALTER TABLE [ZeroVehicleSettings] ADD [LastRecalculatedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104161649_CatalogRecalculationWatermark'
)
BEGIN
    ALTER TABLE [ZeroVehicleSettings] ADD [RecalculationRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104161649_CatalogRecalculationWatermark'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104161649_CatalogRecalculationWatermark', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleIssues] ADD [PublishedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleIssues] ADD [ResolvedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleIssues] ADD [ResolvedListingId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleIssues] ADD [SourceUrl] nvarchar(2048) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [AcceptedListings] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [CalculatedPriceGroups] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [ExtractedListings] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [NewRawMessages] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [PricingCompletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [RejectedListings] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [RejectionReasons] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [ReportVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    ALTER TABLE [TelegramCollectionJobs] ADD [StageErrors] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104163400_PricingExecutionReportsAndIssueProvenance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104163400_PricingExecutionReportsAndIssueProvenance', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104164344_ZeroListingIdentityProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [ExtractionVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104164344_ZeroListingIdentityProvenance'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [RawCarName] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104164344_ZeroListingIdentityProvenance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104164344_ZeroListingIdentityProvenance', N'10.0.7');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104171652_UsedPriceRecalculationRequests'
)
BEGIN
    ALTER TABLE [TelegramPricingStates] ADD [PricingRequestCompletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104171652_UsedPriceRecalculationRequests'
)
BEGIN
    ALTER TABLE [TelegramPricingStates] ADD [PricingRequestedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261104171652_UsedPriceRecalculationRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261104171652_UsedPriceRecalculationRequests', N'10.0.7');
END;

COMMIT;
GO
