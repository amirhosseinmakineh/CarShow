using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarShow.Infrastracture.Migrations;

public partial class TelegramDailyPricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Earlier releases provisioned these tables at startup, outside migrations.
        // Reconcile both existing deployments and databases built only from migrations.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.CarPriceHistories', N'U') IS NULL
            BEGIN
            CREATE TABLE [dbo].[CarPriceHistories] (
            [Id] bigint NOT NULL IDENTITY(1,1),
            [CarId] bigint NOT NULL,
            [MarketPrice] decimal(18,2) NOT NULL,
            [FactoryPrice] decimal(18,2) NOT NULL,
            [Date] datetime2 NOT NULL,
            [IsDelete] bit NOT NULL CONSTRAINT [DF_CarPriceHistories_IsDelete] DEFAULT (0),
            CONSTRAINT [PK_CarPriceHistories] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_CarPriceHistories_Cars_CarId] FOREIGN KEY ([CarId]) REFERENCES [dbo].[Cars] ([Id]) ON DELETE CASCADE
            );
            CREATE INDEX [IX_CarPriceHistories_CarId] ON [dbo].[CarPriceHistories] ([CarId]);
            END;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'SourceUrl') IS NULL ALTER TABLE [dbo].[Cars] ADD [SourceUrl] nvarchar(max) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'IsUsed') IS NULL ALTER TABLE [dbo].[Cars] ADD [IsUsed] bit NOT NULL CONSTRAINT [DF_Cars_IsUsed] DEFAULT (0);
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NULL ALTER TABLE [dbo].[Cars] ADD [FactoryPrice] decimal(18,2) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NULL ALTER TABLE [dbo].[Cars] ADD [MarketPrice] decimal(18,2) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'Slug') IS NULL ALTER TABLE [dbo].[Cars] ADD [Slug] nvarchar(450) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NULL ALTER TABLE [dbo].[Cars] ADD [LastUpdated] datetime2 NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'CategoryId') IS NOT NULL ALTER TABLE [dbo].[Cars] ALTER COLUMN [CategoryId] bigint NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'TipId') IS NOT NULL ALTER TABLE [dbo].[Cars] ALTER COLUMN [TipId] bigint NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'StartDate') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.Cars') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Cars'), 'StartDate', 'ColumnId')) ALTER TABLE [dbo].[Cars] ADD CONSTRAINT [DF_Cars_StartDate] DEFAULT (N'') FOR [StartDate];
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'Price') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.Cars') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Cars'), 'Price', 'ColumnId')) ALTER TABLE [dbo].[Cars] ADD CONSTRAINT [DF_Cars_Price] DEFAULT (0) FOR [Price];
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'ImageName') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.Cars') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Cars'), 'ImageName', 'ColumnId')) ALTER TABLE [dbo].[Cars] ADD CONSTRAINT [DF_Cars_ImageName] DEFAULT (N'') FOR [ImageName];
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'Description') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.Cars') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Cars'), 'Description', 'ColumnId')) ALTER TABLE [dbo].[Cars] ADD CONSTRAINT [DF_Cars_Description] DEFAULT (N'') FOR [Description];
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'IsDelete') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('dbo.Cars') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('dbo.Cars'), 'IsDelete', 'ColumnId')) ALTER TABLE [dbo].[Cars] ADD CONSTRAINT [DF_Cars_IsDelete] DEFAULT (0) FOR [IsDelete];
            """);
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramChannels', N'U') IS NULL
            BEGIN
            CREATE TABLE [dbo].[TelegramChannels] ([Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [IsDelete] bit NOT NULL DEFAULT(0),
                [TelegramIdentifier] nvarchar(256) NOT NULL,
                [NormalizedIdentifier] nvarchar(256) NOT NULL,
                [Username] nvarchar(256) NOT NULL,
                [PublicUrl] nvarchar(512) NULL,
                [Title] nvarchar(512) NOT NULL DEFAULT(N''),
                [Status] nvarchar(64) NOT NULL,
                [Enabled] bit NOT NULL DEFAULT(1),
                [CollectorType] nvarchar(64) NOT NULL,
                [LastSuccessAt] datetime2 NULL,
                [LastAttemptAt] datetime2 NULL,
                [LastError] nvarchar(max) NULL,
                [MessagesCollected] int NOT NULL DEFAULT(0),
                [ListingsExtracted] int NOT NULL DEFAULT(0),
                [RetryCount] int NOT NULL DEFAULT(0),
                [CreatedAt] datetime2 NOT NULL,
                [UpdatedAt] datetime2 NOT NULL);
            CREATE UNIQUE INDEX [IX_TelegramChannels_NormalizedIdentifier] ON [dbo].[TelegramChannels]([NormalizedIdentifier]);
            CREATE UNIQUE INDEX [IX_TelegramChannels_PublicUrl] ON [dbo].[TelegramChannels]([PublicUrl]);
            END;
            """);
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramRawMessages', N'U') IS NULL
            BEGIN
            CREATE TABLE [dbo].[TelegramRawMessages] ([Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [IsDelete] bit NOT NULL DEFAULT(0),
                [ChannelId] bigint NOT NULL,
                [ExternalMessageId] nvarchar(256) NOT NULL,
                [MessageText] nvarchar(max) NOT NULL,
                [MessageDate] datetime2 NOT NULL,
                [MediaMetadata] nvarchar(max) NULL,
                [SourceUrl] nvarchar(1024) NULL,
                [CollectedAt] datetime2 NOT NULL,
                [ProcessingStatus] nvarchar(64) NOT NULL,
                [ProcessingError] nvarchar(max) NULL, CONSTRAINT [FK_TelegramRawMessages_TelegramChannels] FOREIGN KEY([ChannelId]) REFERENCES [dbo].[TelegramChannels]([Id]) ON DELETE CASCADE);
            CREATE UNIQUE INDEX [IX_TelegramRawMessages_ChannelId_ExternalMessageId] ON [dbo].[TelegramRawMessages]([ChannelId],
                [ExternalMessageId]);
            END;
            """);
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramCollectionJobs', N'U') IS NULL
            BEGIN
            CREATE TABLE [dbo].[TelegramCollectionJobs] ([Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [IsDelete] bit NOT NULL DEFAULT(0),
                [ChannelId] bigint NOT NULL,
                [Status] nvarchar(64) NOT NULL,
                [Attempt] int NOT NULL DEFAULT(0),
                [CreatedAt] datetime2 NOT NULL,
                [StartedAt] datetime2 NULL,
                [CompletedAt] datetime2 NULL,
                [Error] nvarchar(max) NULL, CONSTRAINT [FK_TelegramCollectionJobs_TelegramChannels] FOREIGN KEY([ChannelId]) REFERENCES [dbo].[TelegramChannels]([Id]) ON DELETE CASCADE);
            CREATE INDEX [IX_TelegramCollectionJobs_ChannelId_CreatedAt] ON [dbo].[TelegramCollectionJobs]([ChannelId],
                [CreatedAt]);
            END;
            """);
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.CarListings', N'U') IS NULL
            BEGIN
            CREATE TABLE [dbo].[CarListings] ([Id] bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [IsDelete] bit NOT NULL DEFAULT(0),
                [Brand] nvarchar(256) NULL,
                [Model] nvarchar(256) NULL,
                [Trim] nvarchar(256) NULL,
                [Year] int NULL,
                [Mileage] int NULL,
                [Price] decimal(18,2) NULL,
                [Currency] nvarchar(32) NULL,
                [City] nvarchar(128) NULL,
                [BodyCondition] nvarchar(128) NULL,
                [Transmission] nvarchar(128) NULL,
                [Fuel] nvarchar(128) NULL,
                [Color] nvarchar(128) NULL,
                [ChannelId] bigint NOT NULL,
                [SourceChannel] nvarchar(256) NOT NULL,
                [SourceMessageId] nvarchar(256) NOT NULL,
                [SourceUrl] nvarchar(1024) NULL,
                [RawText] nvarchar(max) NOT NULL,
                [ExtractedAt] datetime2 NOT NULL,
                [DeduplicationKey] nvarchar(512) NOT NULL);
            CREATE UNIQUE INDEX [IX_CarListings_ChannelId_SourceMessageId] ON [dbo].[CarListings]([ChannelId],
                [SourceMessageId]);
            END;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'Price') IS NOT NULL
                EXEC(N'UPDATE dbo.Cars SET MarketPrice = CONVERT(decimal(18,2), Price) WHERE MarketPrice IS NULL');
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'MarketPrice') IS NOT NULL BEGIN UPDATE [dbo].[Cars] SET [MarketPrice] = 0 WHERE [MarketPrice] IS NULL; ALTER TABLE [dbo].[Cars] ALTER COLUMN [MarketPrice] decimal(18,2) NOT NULL; END;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'FactoryPrice') IS NOT NULL BEGIN UPDATE [dbo].[Cars] SET [FactoryPrice] = 0 WHERE [FactoryPrice] IS NULL; ALTER TABLE [dbo].[Cars] ALTER COLUMN [FactoryPrice] decimal(18,2) NOT NULL; END;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'LastUpdated') IS NOT NULL BEGIN UPDATE [dbo].[Cars] SET [LastUpdated] = GETUTCDATE() WHERE [LastUpdated] IS NULL; ALTER TABLE [dbo].[Cars] ALTER COLUMN [LastUpdated] datetime2 NOT NULL; END;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'MarketPriceDay') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [MarketPriceDay] date NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'PriceCalculatedAt') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [PriceCalculatedAt] datetime2 NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.Cars', 'PriceSampleCount') IS NULL
                ALTER TABLE [dbo].[Cars] ADD [PriceSampleCount] int NOT NULL DEFAULT (0);
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'CarId') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [CarId] bigint NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'RawMessageId') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [RawMessageId] bigint NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'IsUsed') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [IsUsed] bit NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'NormalizedPrice') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [NormalizedPrice] decimal(18,2) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'PublishedAt') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [PublishedAt] datetime2 NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'ReceivedAt') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [ReceivedAt] datetime2 NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'PriceDay') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [PriceDay] date NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'MatchStatus') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [MatchStatus] nvarchar(32) NOT NULL DEFAULT (N'Pending');
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'MatchReason') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [MatchReason] nvarchar(max) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarListings', 'Fingerprint') IS NULL
                ALTER TABLE [dbo].[CarListings] ADD [Fingerprint] nvarchar(64) NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarPriceHistories', 'PriceDay') IS NULL
                ALTER TABLE [dbo].[CarPriceHistories] ADD [PriceDay] date NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarPriceHistories', 'SampleCount') IS NULL
                ALTER TABLE [dbo].[CarPriceHistories] ADD [SampleCount] int NOT NULL DEFAULT (0);
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarPriceHistories', 'CalculatedAt') IS NULL
                ALTER TABLE [dbo].[CarPriceHistories] ADD [CalculatedAt] datetime2 NULL;
            """);
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.CarPriceHistories', 'IsFinal') IS NULL
                ALTER TABLE [dbo].[CarPriceHistories] ADD [IsFinal] bit NOT NULL DEFAULT (0);
            """);
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.TelegramPricingStates', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TelegramPricingStates (
                    Id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    IsDelete bit NOT NULL DEFAULT (0),
                    LastSuccessAt datetime2 NULL,
                    LastAttemptAt datetime2 NULL,
                    LastError nvarchar(max) NULL
                );
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CarListings_Cars_CarId')
                ALTER TABLE dbo.CarListings ADD CONSTRAINT FK_CarListings_Cars_CarId
                    FOREIGN KEY (CarId) REFERENCES dbo.Cars(Id);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CarListings_TelegramRawMessages_RawMessageId')
                ALTER TABLE dbo.CarListings ADD CONSTRAINT FK_CarListings_TelegramRawMessages_RawMessageId
                    FOREIGN KEY (RawMessageId) REFERENCES dbo.TelegramRawMessages(Id);
            CREATE UNIQUE INDEX IX_CarListings_RawMessageId ON dbo.CarListings(RawMessageId)
                WHERE RawMessageId IS NOT NULL;
            CREATE INDEX IX_CarListings_CarId_PriceDay ON dbo.CarListings(CarId, PriceDay);
            CREATE UNIQUE INDEX IX_CarPriceHistories_CarId_PriceDay ON dbo.CarPriceHistories(CarId, PriceDay)
                WHERE PriceDay IS NOT NULL;
            -- Keep every job; mark redundant wakeups failed before enforcing one active job per channel.
            WITH Jobs AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY ChannelId ORDER BY Id) AS Position
                FROM dbo.TelegramCollectionJobs WHERE Status IN ('Queued', 'Processing')
            )
            UPDATE Jobs SET Status = 'Failed', Error = 'Superseded duplicate job during migration.'
                WHERE Position > 1;
            CREATE UNIQUE INDEX IX_TelegramCollectionJobs_ChannelId ON dbo.TelegramCollectionJobs(ChannelId)
                WHERE Status IN ('Queued', 'Processing');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A schema rollback would discard observations. Roll back the application while retaining the schema.
        throw new NotSupportedException("This migration preserves production history and requires an explicit data-preserving rollback script.");
    }
}
