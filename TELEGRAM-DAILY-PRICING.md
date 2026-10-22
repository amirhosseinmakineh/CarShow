# Telegram daily pricing implementation

Base: `master` at `e8ba50d85f64873efd7c203fc9a2f69829ad4dcf`.

## Removed

Removed the Car.ir site adapter, crawler sync application service, crawler contracts/data DTOs,
GetCarsBackgroundService, crawler configuration and DI, and CarCrawlerController.
Removed the stale tracked `CarShow.zip` source archive, which contained the retired implementation.
Existing Cars and CarPriceHistories are retained, along with shared display DTOs and stored detail JSON.
HtmlAgilityPack and WTelegramClient remain necessary for Telegram; no crawler-only package remained.
The frontend base had no remaining site-crawler controls to delete.

## Processing and matching

Infrastructure owns Telegram network access, EF persistence and SQL application locks.
Collector contracts are in Domain. Collection orchestration, extraction, matching, validation and
arithmetic pricing are in ApplicationService. The two BackgroundServices are in the API project
and only invoke application services and schedule work.

Existing TelegramRawMessage and CarListing are reused. CarListing now carries CarId, RawMessageId,
condition, original currency amount, normalized toman amount, publication/receipt timestamps,
Tehran PriceDay, match status/reason and a fingerprint. Unparseable messages also get an audit listing.
Statuses are Pending, Matched, Unmatched, Ambiguous, Invalid, Late and CalculationError.

Matching normalizes Persian/Arabic digits, Arabic kaf/yeh, whitespace and zero-width non-joiners.
Company and model must match complete text tokens; a configured trim must also match.
A generic catalog row is never selected when that company/model has typed variants.
Missing/contradictory new/used evidence is invalid. More than one full candidate is ambiguous.
There are no inferred catalog aliases, fuzzy token matches or automatic car creation.
The actual catalog has no model-year identity field: parsed year is retained, but cannot select a year-specific car.
Brand/model/trim fields are filled from the unique matched catalog identity.

The conservative parser accepts one labeled price with explicit IRT/toman or IRR/rial currency and
optional explicit thousand/million/billion scale. Unknown units, multiple labeled prices,
nonpositive/unparseable/out-of-storage-range amounts are excluded. A list containing several
vehicles/prices is retained for inspection rather than split or guessed. Price retains the
amount in its original currency after expansion of the explicit scale; RawText retains the exact source expression.

## Daily calculation and time contract

All timestamp columns use UTC DateTime, matching existing Telegram persistence. PriceDay is the
publication timestamp converted to Asia/Tehran. Receipt time does not assign a message to today.
The canonical currency is toman, consistent with the current car UI: IRR is divided by decimal 10.

For each car and open Tehran day, use decimal arithmetic mean of positive valid observations.
Exact normalized full-text reposts within the same car/day share a SHA-256 fingerprint and have
one vote. Different text is retained independently; no price-only or speculative cross-channel deduplication.
The result is rounded to the existing decimal(18,2) storage precision, midpoint away from zero.
No observations means no synthetic price/history; prior MarketPrice remains intact.
Only MarketPrice is updated. FactoryPrice is never changed by Telegram.
A late correction to an older day cannot replace a newer MarketPriceDay.

CarPriceHistory is extended with nullable PriceDay, SampleCount, CalculatedAt and IsFinal.
Legacy history keeps a null PriceDay and is never deleted or rewritten. A filtered unique
(CarId, PriceDay) index makes daily history updates idempotent. Car stores the latest calculation metadata.
Raw messages, linked listings and active collection jobs also have database uniqueness constraints.
SQL Server transaction-owned application locks serialize collection and pricing across API instances;
a busy instance leaves the other instance's job/result alone. SQLite transactions exercise concurrency in tests.

## Scheduling and late arrivals

- Telegram:CollectionIntervalMinutes = 15: continuously revisit enabled channels, including Active/Error channels.
- Telegram:CalculationIntervalSeconds = 60: recalculate open days and recover unfinished pricing immediately after restart.
- Telegram:DailyCalculationTimeTehran = 00:15: local finalization cutoff, HH:mm.
- Telegram:LateArrivalDays = 2: keep today plus the preceding two days open after the daily cutoff.
- Existing Telegram:Provider, TimeoutSeconds, MaxRetries, RequestDelayMilliseconds,
  HistoryPerChannel and optional MTProto configuration remain supported.

With the defaults, Tehran day D is finalized by the first cycle at/after 00:15 on D+3.
Until then, new observations update the same history row. After finalization, newly processed
messages from that day are retained as Late and excluded. An outage longer than the open window
does not retroactively reopen finalized days. Pending messages/listings are recoverable from SQL;
processing is batched at 2,000 previously unlisted messages per cycle. No in-memory daily job marker is required.
Interrupted collection jobs are recovered from SQL. Existing timeout, bounded retries and inter-request delay remain.
A channel failure does not stop the worker, and per-car calculation errors retain the previous price
and appear on the affected observations. Cycle-level success/error is persisted in TelegramPricingState.

Web Preview still requires neither bot administrator rights nor ApiId/ApiHash. Public preview has
Telegram's bounded visible history, not guaranteed archive coverage. Configure polling and the
collection window for the posting volume; deleted/unavailable posts cannot be reconstructed.

## API and frontend contract

Admin JWT is required for `api/admin/telegram-pricing/{observations,prices,state}`.
Observations support status + zero-based page; prices support optional carId + zero-based page.
Both lists use 100-row pages. State reports persisted last success/attempt/error.
Car list/detail DTOs also return MarketPriceDay, PriceSampleCount and UTC PriceCalculatedAt.
The matching frontend PR displays prices, history, source links, statuses/reasons and collection/calculation errors.

## Migration / deployment

Migration: `20261019162709_TelegramDailyPricing`, with updated EF designer and model snapshot.
Earlier releases provisioned Telegram/history tables at API startup instead of through migrations;
the new migration reconciles these tables using guarded creation before adding nullable links and metadata.
New database uniqueness indexes do not rewrite old price history. Redundant active job rows are marked
Failed, preserving their audit records. The old Price/StartDate/Category columns are retained.
Where MarketPrice is missing/null, an existing legacy Price is copied before filling remaining nulls.
Raw startup schema SQL and PendingModelChangesWarning suppression were removed.
The migration contains no DROP TABLE, DROP COLUMN or destructive rename.
Down intentionally refuses destructive rollback: deploy the older application against the retained
additive schema, or prepare an explicit data-preserving rollback script.

Review/apply this migration to a backed-up SQL Server deployment with schema-change permission;
the existing startup MigrateAsync path remains. No migration was applied to a live production database here.

## Verification

Executed successfully: dotnet restore, dotnet build, and all 29 tests (0 failures, 0 skipped;
approximately 32 seconds). An idempotent SQL Server migration script was generated and reviewed;
EF reports no pending model changes. The script was not executed against SQL Server.
Frontend production build and test-source TypeScript compilation also passed.
The automated suite covers matching, trim/condition separation, ambiguity, Unicode normalization,
explicit currency conversion, invalid prices, arithmetic mean, exact-text repost deduplication,
Tehran midnight, receipt versus publication day, no-data preservation, daily reruns and new-data updates,
finalization, SQL-backed restart recovery, concurrency, continuous polling, error isolation and 200 channels.
Collection tests use test-only HTML/HTTP fixtures through the production preview parser; they do not establish live Telegram access.

Live Telegram returned a Site Unavailable intermediary page in this environment. No local SQL Server
or container runtime was available; SQLite integration tests do not validate SQL Server applocks or actual migration execution.
Chromium was not installed and its download failed; Angular browser tests could not launch.
Existing restore warnings include Microsoft.OpenApi, SQLitePCLRaw and System.Security.Cryptography.Pkcs advisories;
existing nullable warnings remain outside this task. No credentials were added to the frontend or report.

## Changed paths

- `CarShow.ApplicationService.Contract/Dtos/CarDto/CarDto.cs`
- `CarShow.ApplicationService.Contract/Dtos/Telegram/TelegramPricingDtos.cs`
- `CarShow.ApplicationService.Contract/IService/ITelegramPricingQueries.cs`
- `CarShow.ApplicationService/CarShow.ApplicationService.csproj`
- `CarShow.ApplicationService/Services/CarCrawlerSyncService.cs`
- `CarShow.ApplicationService/Services/CarListingMatcher.cs`
- `CarShow.ApplicationService/Services/CarService.cs`
- `CarShow.ApplicationService/Services/TelegramCollectionService.cs`
- `CarShow.ApplicationService/Services/TelegramListingExtractor.cs`
- `CarShow.ApplicationService/Services/TelegramPricingQueries.cs`
- `CarShow.ApplicationService/Services/TelegramPricingService.cs`
- `CarShow.Domain/Crawling/CarSourceData.cs`
- `CarShow.Domain/Crawling/ICarCrawlerSyncService.cs`
- `CarShow.Domain/Crawling/ICarDataSource.cs`
- `CarShow.Domain/IRepository/ITelegramProcessingStore.cs`
- `CarShow.Domain/Models/Car.cs`
- `CarShow.Domain/Models/CarPriceHistory.cs`
- `CarShow.Domain/Models/Telegram/TelegramChannel.cs`
- `CarShow.Domain/Models/Telegram/TelegramPricingState.cs`
- `CarShow.Domain/Telegram/`
- `CarShow.Infrastracture/BackgroundServices/GetCarsBackgroundService.cs`
- `CarShow.Infrastracture/Configuration/CrawlerSettings.cs`
- `CarShow.Infrastracture/Context/CarShowContext.cs`
- `CarShow.Infrastracture/Crawlers/CarIrCrawlerService.cs`
- `CarShow.Infrastracture/Crawlers/DTOs/CarPriceCrawlerDto.cs`
- `CarShow.Infrastracture/Crawlers/ICarIrCrawlerService.cs`
- `CarShow.Infrastracture/Migrations/20261019162709_TelegramDailyPricing.Designer.cs`
- `CarShow.Infrastracture/Migrations/20261019162709_TelegramDailyPricing.cs`
- `CarShow.Infrastracture/Migrations/CarShowContextModelSnapshot.cs`
- `CarShow.Infrastracture/Repository/BaseRepository.cs`
- `CarShow.Infrastracture/Repository/TelegramProcessingStore.cs`
- `CarShow.Infrastracture/Telegram/ITelegramCollector.cs`
- `CarShow.Infrastracture/Telegram/TelegramCollectionWorker.cs`
- `CarShow.Infrastracture/Telegram/TelegramCollector.cs`
- `CarShow.Infrastracture/Telegram/TelegramListingExtractor.cs`
- `CarShow.Tests/TelegramCollectionTests.cs`
- `CarShow.Tests/TelegramEndpointTests.cs`
- `CarShow.Tests/TelegramPricingTests.cs`
- `CarShow.zip`
- `CarShow/BackgroundServices/`
- `CarShow/Controllers/CarCrawlerController.cs`
- `CarShow/Controllers/TelegramPricingController.cs`
- `CarShow/Program.cs`
- `CarShow/appsettings.Development.json`
- `CarShow/appsettings.json`
