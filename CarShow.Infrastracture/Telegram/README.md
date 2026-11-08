# Attached Telegram implementation

Production has one Telegram entrypoint: `TelegramImportController`, adapted from the actual attachment `Pasted text(20261108-182753).txt` (the task text names `181901`). The reader, parser, normalization, price conversion and reference builder retain the supplied code. Their source SHA-256 is checked by the test runner; the only accessibility change exposes the existing converter to the mapping method.

- Admin JWT required: `GET /api/telegram-cars/scan` retrieves the exact eight attached channels and inserts **every parser result** into existing `CarShowContext.Cars`, before reference grouping. The returned references retain the supplied algorithm.
- Mapping: `Name` is the parser name; `Description` is JSON containing the exact parsed `Name`, `Price`, `Year`; `MarketPrice` is the supplied converter's billion-toman result multiplied by 1,000,000,000. For a missing/unconvertible price the raw null/text is retained and the existing non-nullable numeric column uses 0. All three catalog FKs are null. No catalog rows/IDs are created. `LastUpdated` is UTC.
- The real model FK is `CarModelId` (`long?`), not `ModelId`. CompanyId and TipId are also `long?`; configuration makes all three relations optional.
- Any retrieval error returns 502 with channel errors and inserts nothing, including partial channel failures. This is an error-reporting guard, not an extraction rule. The supplied 422 response is retained when references are empty; parser ads have already been saved at that point.
- `GET /api/telegram-cars/diagnostics` and `POST /api/telegram-cars/diagnostics/parsed-cars` do not write to the database.
- The old Telegram scanner, controller, job manager, DI registrations and Telegram configuration are removed. Old async job/raw-preview endpoints no longer exist. Car.ir, its crawler job, unrelated APIs and existing database rows are untouched.
- Import is on demand, as in the attachment. Repeating a scan inserts the parser results again; no extra deduplication, scheduling, trim rules, outlier removal or source-selection rules were added.

## Validation

`dotnet build CarShow.slnx --configuration Release`

`dotnet run --project tests/TelegramCarScanner.Tests/TelegramCarScanner.Tests.csproj --configuration Release`

Tests use actual Telegram HTML fixtures and the supplied reader, then persist into a real file-backed SQLite database through the production context/controller and reopen the database in a separate context. They check each nullable FK, raw years, individual prices, an unmatched singleton, missing fields, existing-row preservation, connection failures, cancellation and reference behavior. SQL Server migration operations and SQL generation are also checked. SQLite is not evidence of compatibility with an unseen production SQL Server schema.

Migration: `20261108223000_MakeCarCatalogReferencesOptional` alters only the three existing Cars FK columns and recreates their FKs. It performs no data deletion. Down refuses rollback if null references exist instead of inventing IDs.

Production connection strings are empty in this checkout. Before deployment, inspect the actual Cars schema and apply the migration against that verified schema. Historical migrations/snapshot predate the current entity: they still describe legacy required `StartDate`/`Price` columns and other catalog fields. If those required legacy columns still exist without defaults, the current entity cannot insert into that schema; do not claim production insert success based on the SQLite test. No unrelated schema repair is included in this change.

Observed local validation (2026-11-08): solution build succeeded with zero errors (existing package/nullability warnings remain); all 36 checks passed. A live `--live` diagnostic used the unmodified reader against all eight attached channels and returned 502: every channel's HTML lacked `data-post` elements. No alternative reader was added and this diagnostic made no database writes. Live Telegram extraction and production SQL Server insertion therefore remain unverified.
