# Attached Telegram implementation

Production has one Telegram entrypoint: `TelegramImportController`, adapted from the actual attachment `Pasted text(20261108-182753).txt` (the task text names `181901`). The reader, parser, normalization and price converter retain the supplied code. Their source SHA-256 is checked by the test runner; the reference builder retains the same valid-name/year rules, minimum two advertisements, and simple arithmetic mean while converting all valid prices to toman before averaging.

- Admin JWT required: `GET /api/telegram-cars/scan` retrieves the exact eight attached channels and inserts **one final result per eligible reference group** into existing `CarShowContext.Cars`, after grouping and averaging. Raw parser results remain in memory and are not inserted.
- Mapping: `Name` is the reference group name; `Description` is JSON with `Type=TelegramPriceReference`, `Year`, advertisement count, compared prices, minimum, maximum, and reference price; `MarketPrice` is the reference price converted to toman. Invalid/missing price or year never creates a Cars row. All three catalog FKs are null. No catalog rows/IDs are created. `LastUpdated` is UTC.
- The real model FK is `CarModelId` (`long?`), not `ModelId`. CompanyId and TipId are also `long?`; configuration makes all three relations optional.
- Retrieval failures are isolated by channel. Fully failed retrieval returns 502 and inserts nothing. Successful channels can still produce final references when another channel fails; that response is 207 with Status=PartialSuccess and ChannelErrors, never an unqualified success. Partial messages from a failed channel are excluded from comparison. The 422 response reports `SavedCars=0` when references are empty; nothing is inserted.
- `GET /api/telegram-cars/diagnostics` and `POST /api/telegram-cars/diagnostics/parsed-cars` do not write to the database.
- The old Telegram scanner, controller, job manager, DI registrations and Telegram configuration are removed. Old async job/raw-preview endpoints no longer exist. Car.ir, its crawler job, unrelated APIs and existing database rows are untouched.
- Import is on demand, as in the attachment. Repeating a scan inserts the final reference groups again; no extra deduplication, scheduling, trim rules, outlier removal or source-selection rules were added.

## Validation

`dotnet build CarShow.slnx --configuration Release`

`dotnet run --project tests/TelegramCarScanner.Tests/TelegramCarScanner.Tests.csproj --configuration Release`

Tests use actual Telegram HTML fixtures and the supplied reader, then persist into a real file-backed SQLite database through the production context/controller and reopen the database in a separate context. They check each nullable FK, a singleton (zero inserts), same-name/same-year average (one insert), different years (zero inserts), missing/invalid data (zero inserts), multiple groups (one insert per group), final comparison details, existing-row preservation, connection failures and cancellation. SQL Server migration operations and SQL generation are also checked. SQLite is not evidence of compatibility with an unseen production SQL Server schema.

Migration: `20261108223000_MakeCarCatalogReferencesOptional` alters only the three existing Cars FK columns and recreates their FKs. It performs no data deletion. Down refuses rollback if null references exist instead of inventing IDs.

Production connection strings are empty in this checkout. Before deployment, inspect the actual Cars schema and apply the migration against that verified schema. Historical migrations/snapshot predate the current entity: they still describe legacy required `StartDate`/`Price` columns and other catalog fields. If those required legacy columns still exist without defaults, the current entity cannot insert into that schema; do not claim production insert success based on the SQLite test. No unrelated schema repair is included in this change.

Observed validation (2026-11-09): solution build succeeded with zero errors (existing package/nullability warnings remain); 68 checks passed, including production controller insertion and separate-context read-back against file-backed SQLite for singleton, same-year average, different-year and invalid inputs, and multiple valid reference groups. The prior live `--live` diagnostic used the unmodified reader against all eight attached channels and returned 502: every channel's HTML lacked `data-post` elements. No alternative reader was added and this diagnostic made no database writes. Live Telegram extraction and production SQL Server insertion remain unverified.

## Diagnostic API behavior

- `GET /api/telegram-cars/diagnostics` retrieves current Telegram posts, parses and calculates metrics/examples in memory; it never saves Cars. Full retrieval failure returns 502; partial retrieval returns 207 with channel errors and healthy-channel metrics.
- `POST /api/telegram-cars/diagnostics/parsed-cars` accepts already parsed name/year/price objects and returns eligibility/grouping/reference results without contacting Telegram or changing the database.
- Diagnostic example/reference `Year` values are formatted at the response boundary only. An existing Persian year is preserved. A Gregorian year such as 2024 is displayed as 1402/1403 because no month/day is available to choose one Persian year. Input years, grouping keys and stored years are unchanged. These APIs expose no post timestamps or times; no time-zone offset is applied to a year-only value. LastUpdated storage remains UTC.
- The reported live Scan 500 is not yet diagnosed: server Exception/StackTrace and authenticated deployment access are unavailable. Do not treat these channel-isolation/display corrections as proof that the production persistence exception is fixed.
