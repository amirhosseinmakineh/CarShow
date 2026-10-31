# Independent zero vehicle ingestion and experimental pricing

This feature is prepared on `feature/independent-zero-vehicles`. It must be reviewed before merging or deploying. The old collector, extraction, registration, pricing services, controllers, and entity mappings are unchanged. The only shared runtime dependency is the existing `ITelegramCollector` public HTML transport. No Telegram account, AI, or paid API is used.

## Data boundary

New tables: `ZeroVehicleModels`, `ZeroVehicleTrims`, `ZeroVehicleListings`, `ZeroVehicleImportRuns`, `ZeroVehiclePriceGroups`. None references `Cars`, `CarListings`, `TelegramChannels`, `TelegramRawMessages`, or their price history. Model-scoped trim IDs have composite foreign keys. Unique `(Channel, ExternalMessageId)` prevents repeated imports from inserting another accepted ad. Rejected message bodies are never persisted; a run holds only aggregate rejection codes/counts. Catalog rows and aggregate run reports can exist when all messages are rejected.

Migration: `20261030213449_IndependentZeroVehicles`. Its Up script creates only these five tables and their indexes/foreign keys; it does not alter or drop an existing table. The Down operation drops only the new zero-vehicle tables. SQL Server migration SQL was generated and reviewed; it was not executed against a production database.

## Validation and configurable catalog

Digits, Arabic/Persian letters and spacing are normalized. Explicit vehicle condition claims (`صفر`, `صفرکیلومتر`, `0 کیلومتر`, `خشک`, `کارنکرده`, `بدون کارکرد`) are accepted only after rejecting positive numeric odometers and contradictory used/nonzero wording. Accessory claims such as `لاستیک صفر`, `رنگ صفر`, `خرج صفر`, and `صفر تا صد` are removed before evaluating condition. Unknown/ambiguous model or trim, installment/negotiable/multiple price, unconvertible currency, invalid/future date and invalid message IDs are rejected before insertion. Input bodies are capped at 8000 characters.

The independent model/trim catalog uses `ZeroVehicles:Catalog` in configuration. Initial explicit examples cover Peugeot 207, Shahin, Tara, Dena Plus and KMC J7; this is a deliberately limited initial catalog, not recognition of every car. Names/aliases can be extended without modifying processor code. A trim is scoped to its model. A longer configured trim alias takes precedence over an alias wholly contained in it, while conflicting separate trims are rejected. Avoid defining a broad alias for multiple distinct trims. Unknown trim variants require configuration rather than guessing. Configured catalog changes are synchronized during the next import; existing identities are retained. Renaming a canonical entry creates a distinct identity, so add aliases instead when spelling alone changes.

Example:

```json
{
  "ZeroVehicles": {
    "WorkerEnabled": true,
    "MaxPriceAgeDays": 7,
    "MinimumPriceToman": 10000000,
    "MaximumPriceToman": 100000000000,
    "Catalog": [{
      "Name": "پژو 207",
      "Aliases": ["207", "peugeot 207"],
      "Trims": [{"Name": "اتوماتیک پانوراما", "Aliases": ["اتومات پانوراما", "پانوراما اتومات"]}]
    }]
  }
}
```

Prices normalize to toman. Explicit rial divides by ten; scaled million/billion prices use toman when no currency is printed. An unscaled bare value without currency is rejected. Optional year is extracted when labeled and four digits; absence is null. Year never participates in the comparison key.

## Jobs and pricing

POST import returns HTTP 202 with a persisted queued run; the independent hosted worker claims it with a transaction/application lock. The worker reuses Collector's limit (1–500) and positive `before` cursor, returns `NextBefore`, pages fetched and transport result. A partial HTTP result can still store individually validated messages and is reported `Partial`, never as complete. Crashed claims become eligible after ten minutes; processing has a five-minute timeout. Application locks isolate zero processing from the old workflow. Concurrent requests for the same channel return 409. The listings insertion and price recalculation complete transactionally.

Each group is `(ModelId, TrimId)` across all accepted years. Prices older than the configured age (default seven days, clamp 1–30) or future dates are excluded as `Stale`. Each distinct accepted advertisement contributes once. Identical normalized message fingerprints count once, including copies across channels; reposted ads remain accepted records but do not inflate price samples. Distinct ads from the same channel can contribute, while confidence still counts independent channels. Absolute bounds are enforced; with at least three distinct advertisement samples, values below half or above twice the median are excluded as `Outlier`. The remaining median is stored with min/max, sample/source counts and calculation time. One sample produces its own price with Low confidence; 2–4 independent sources produce Medium; 5+ High (experimental labels, not a guarantee). With no sample the reference group is removed. Idle workers recalculate every fifteen minutes to expire stale references. Pricing reads/updates listings in batches of 500 and releases tracked listings after each batch; accepted inserts are bounded at 500 per job.

`Accepted` counts valid messages including replayed IDs, `Duplicates` counts already-stored channel/message identities, `Stored` counts new accepted ads, and `PriceGroups` counts currently valid price identities associated with that run's accepted messages. One model/trim may have multiple accepted ads. Rejections are run-local aggregate statistics only.

## Admin API

All routes require the existing Admin authentication/authorization and start with `/api/admin/zero-vehicles`.

| Method | Path | Result |
|---|---|---|
| POST | `/imports` | `{channel,messageLimit,before?}` → persisted run, HTTP 202 |
| GET | `/imports` | Paginated run reports |
| GET | `/imports/{id}` | Job status, counts, cursor and transport errors |
| GET | `/catalog` | Standard model/trim IDs |
| GET | `/listings` | Accepted ads; search/modelId/trimId/status/page/pageSize |
| GET | `/listings/{id}` | Accepted listing and raw text |
| GET | `/prices` | Reference groups; search/page/pageSize |
| GET | `/prices/{id}/listings` | Only Included sample ads for that reference, paginated |
| POST | `/prices/recalculate` | Recalculate/expire references and return group count |

Paged results are `{items,total,page,pageSize}`; pages start at zero; default size 10, maximum 100. Import replay is idempotent. `before` must be positive. The HTTP request only queues work; keep `ZeroVehicles:WorkerEnabled=true` on the application that executes jobs.

Frontend route: `/adminDashboard/zeroVehicles` (also `/admin/dashboard/zero-vehicles`). Its menu item is **دریافت آگهی‌های صفر**. The page provides real job polling/history, aggregate rejection reasons, continuation through Collector's cursor, server filtering/pagination, accepted-ad details and reference sample membership. It does not filter or mutate the existing Telegram page.

## Reproducible verification

```bash
dotnet test CarShow.Tests/CarShow.Tests.csproj --filter 'FullyQualifiedName~ZeroVehicleTests|FullyQualifiedName~TelegramMinimalRegistrationTests|FullyQualifiedName~TelegramReferencePricingTests'
dotnet ef migrations script 20261030110000_OptionalTelegramCondition 20261030213449_IndependentZeroVehicles --project CarShow.Infrastracture --startup-project CarShow
# Uses only a NEW local SQLite database, never the production connection string:
dotnet run --project tools/ZeroVehicleProbe/ZeroVehicleProbe.csproj -- @baravard
```

The probe requires a reachable public Telegram HTML preview and prints its local database path and actual job outcome. A transport failure is a failed job and zero inserted ads, not a successful ingestion demonstration.

Frontend checks (in `mayaCarFrontProduc` feature branch):

```bash
npm run build -- --configuration development
npm test -- --watch=false --browsers=ChromeHeadless --include=src/app/components/admin/zero-vehicles/zero-vehicles.component.spec.ts
```

Use a test database, apply the migration there, start the API with its real Admin token and both worker settings as intended, then POST `/imports` and poll its returned Location. Inspect only the `ZeroVehicle…` tables; verify the existing tables remain unchanged. No production migration, merge or deploy is performed by this task.

## Results in the implementation environment

- 56 focused backend tests passed, zero failed: 29 independent zero-vehicle cases plus 27 existing name/price and reference-pricing regression cases. They execute against local SQLite using the production EF model/store and ASP.NET TestServer, not a mocked repository.
- The HTML collector integration test exercised two pages, the before/nextBefore cursors, rejection of a used ad and persistence of only accepted zero ads. A separate test executes the old workflow before and after a zero import against the same database and verifies its rows/prices remain unchanged.
- EF reported no pending model changes after generation. SQL Server migration SQL contains only CREATE statements for five ZeroVehicle tables plus indexes/foreign keys and migration-history bookkeeping. No production database update was attempted.
- Angular development and production builds passed. The two frontend spec cases compiled; Karma execution was blocked by the missing Chrome binary. Playwright's browser download was attempted and failed (invalid/truncated archive). Browser assertions were therefore not reported as passed.
- Live local SQLite probes for @baravard and @HEZHA_cars both returned PreviewUnavailable with zero scanned/inserted records. Live Telegram ingestion could not be confirmed from this environment. Neither probe accessed a production database.
- mayaCarFront currently has no commits; frontend work is based on mayaCarFrontProduc/main. No empty repository was initialized implicitly.

## Changed files

Backend files (new or extended):

```text
CarShow.ApplicationService.Contract/Dtos/ZeroVehicles/ZeroVehicleDtos.cs
CarShow.ApplicationService/Services/ZeroVehicles/ZeroVehicleRules.cs
CarShow.ApplicationService/Services/ZeroVehicles/ZeroVehicleService.cs
CarShow.Domain/IRepository/IZeroVehicleStore.cs
CarShow.Domain/Models/ZeroVehicles/ZeroVehicleEntities.cs
CarShow.Infrastracture/Context/CarShowContext.cs
CarShow.Infrastracture/Context/ZeroVehicleConfiguration.cs
CarShow.Infrastracture/Migrations/20261030213449_IndependentZeroVehicles.Designer.cs
CarShow.Infrastracture/Migrations/20261030213449_IndependentZeroVehicles.cs
CarShow.Infrastracture/Migrations/CarShowContextModelSnapshot.cs
CarShow.Infrastracture/Repository/ZeroVehicleStore.cs
CarShow.Tests/ZeroVehicleTests.cs
CarShow/BackgroundServices/ZeroVehicleWorker.cs
CarShow/Controllers/ZeroVehiclesController.cs
CarShow/Program.cs
CarShow/appsettings.json
docs/independent-zero-vehicles.md
tools/ZeroVehicleProbe/Program.cs
tools/ZeroVehicleProbe/ZeroVehicleProbe.csproj
```

Frontend files on the companion branch:

```text
src/app/app.routes.ts
src/app/components/admin/zero-vehicles/zero-vehicles.component.css
src/app/components/admin/zero-vehicles/zero-vehicles.component.html
src/app/components/admin/zero-vehicles/zero-vehicles.component.spec.ts
src/app/components/admin/zero-vehicles/zero-vehicles.component.ts
src/app/components/adminDashboard/adminDashboard.component.html
src/app/components/adminDashboard/adminDashboard.component.ts
src/app/services/zero-vehicles.service.ts
```
