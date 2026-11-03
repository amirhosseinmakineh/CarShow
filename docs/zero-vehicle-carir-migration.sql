BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    DROP INDEX [IX_ZeroVehicleListings_Channel_ExternalMessageId] ON [ZeroVehicleListings];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [MarketPrice] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehiclePriceGroups] ADD [PricingStatus] nvarchar(32) NOT NULL DEFAULT N'Unavailable';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [FinalPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketFetchedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketName] nvarchar(512) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketPrice] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketPriceAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketStatus] nvarchar(32) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [MarketUrl] nvarchar(2048) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [PartIndex] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [PriceCandidates] nvarchar(1000) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [RawPrice] nvarchar(256) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleListings] ADD [RejectionReason] nvarchar(1024) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    ALTER TABLE [ZeroVehicleImportRuns] ADD [Extracted] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE TABLE [ZeroVehicleChannels] (
        [Id] bigint NOT NULL IDENTITY,
        [Identifier] nvarchar(64) NOT NULL,
        [Enabled] bit NOT NULL,
        [ScanBefore] bigint NULL,
        [PendingLatestMessageId] bigint NULL,
        [LatestMessageId] bigint NULL,
        [LastImportedAt] datetime2 NULL,
        [LastError] nvarchar(1024) NULL,
        CONSTRAINT [PK_ZeroVehicleChannels] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE TABLE [ZeroVehicleIssues] (
        [Id] bigint NOT NULL IDENTITY,
        [RunId] bigint NOT NULL,
        [ExternalMessageId] nvarchar(32) NOT NULL,
        [PartIndex] int NOT NULL,
        [Code] nvarchar(64) NOT NULL,
        [RawText] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_ZeroVehicleIssues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ZeroVehicleIssues_ZeroVehicleImportRuns_RunId] FOREIGN KEY ([RunId]) REFERENCES [ZeroVehicleImportRuns] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE TABLE [ZeroVehicleSettings] (
        [Id] int NOT NULL IDENTITY,
        [DeviationPercent] decimal(5,2) NOT NULL,
        CONSTRAINT [PK_ZeroVehicleSettings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ZeroVehicleListings_Channel_ExternalMessageId_PartIndex] ON [ZeroVehicleListings] ([Channel], [ExternalMessageId], [PartIndex]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ZeroVehicleChannels_Identifier] ON [ZeroVehicleChannels] ([Identifier]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ZeroVehicleIssues_RunId_ExternalMessageId_PartIndex] ON [ZeroVehicleIssues] ([RunId], [ExternalMessageId], [PartIndex]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261102050507_ZeroVehicleCarIrPipeline'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261102050507_ZeroVehicleCarIrPipeline', N'10.0.7');
END;

COMMIT;
GO
