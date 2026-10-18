# Telegram management fix verification

Base: `master` at `0bff42d239e5747269ea82f6c7441c059e7a15c7`.

## Behavior fixed

- Pause/resume now tracks the channel entity, so changes survive a new scope/reload.
- Bulk import counts duplicates already in SQL as well as duplicates within the request.
- Queued jobs are persisted before worker notification and visible immediately in job logs.
- Worker consumes persisted queued jobs; repeated notifications do not create duplicate attempts.
- Restart recovery closes interrupted attempts and recreates work for enabled Pending/Processing channels.
- Unexpected exceptions as well as transient collector errors get bounded exponential retry; pause is preserved during network collection.
- Collection output and job completion commit together, preventing partially persisted raw data from being silently skipped on retry.
- Unavailable/empty preview HTML no longer reports success; text line breaks and Persian numeric fields are preserved/normalized.
- JWT/Admin authorization and controller route prefix remain intact. Input length/required validation returns HTTP 400.

## Executed checks

- .NET 10 restore and solution/application build succeeded. Existing nullable/package warnings remain; they are not compiler errors.
- Eight automated tests passed: import 200 channels/jobs; duplicate/invalid import; persisted pause/resume; collection/extraction/idempotency; transient failures and limited retries; inaccessible preview; restart recovery; JWT-protected HTTP routes/DTO binding; and draining a 200-job queue (some are combined in one test).
- The 200-job drain had 199 successes and one access failure in 21.15 seconds. Managed GC snapshots: 8,154,120 bytes before; 7,665,000 bytes after. This is not peak RSS or a live Telegram throughput measurement.
- Endpoint tests exercised real ASP.NET routing, JWT validation, controllers and service against SQLite using TestServer. They returned 401 without a token, 200 for the four Admin GETs, 400 for empty import, and a persisted queued job for valid import.

## Not verified here

No SQL Server instance or real Admin login was available. Telegram preview requests returned `Site Unavailable` HTML from this environment. Full browser → API → SQL Server → Telegram collection and SQL Server startup provisioning/migrations must be verified locally. Test HTML/HTTP fixtures exist only in the test project. Production has no fake collector or fake success path.

No new database columns are introduced. Existing startup schema provisioning is preserved. The queue assumes a single application/worker instance. General AI extraction, full Telegram history, recurring scheduled polling, distributed workers and reference-price calculation are outside this fix.

## Changed files

- `CarShow.ApplicationService.Contract/Dtos/Telegram/TelegramDtos.cs`
- `CarShow.ApplicationService/Services/TelegramChannelService.cs`
- `CarShow.Infrastracture/Telegram/TelegramCollectionQueue.cs`
- `CarShow.Infrastracture/Telegram/TelegramCollectionWorker.cs`
- `CarShow.Infrastracture/Telegram/TelegramCollector.cs`
- `CarShow.Infrastracture/Telegram/TelegramListingExtractor.cs`
- `CarShow.Tests/CarShow.Tests.csproj`
- `CarShow.Tests/TelegramCollectionTests.cs`
- `CarShow.Tests/TelegramEndpointTests.cs`
- `CarShow.slnx`
- `CarShow/Controllers/TelegramChannelsController.cs`
- `README-TELEGRAM.md`
- `TELEGRAM-FIX-REPORT.md`
