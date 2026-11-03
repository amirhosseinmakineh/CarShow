> Historical report. The original ten-ad fixture has now been supplied and verified. See [current acceptance report](zero-vehicle-final-acceptance.md) for updated results and remaining blockers.

# CarShow zero-only verification — 2026-11-02

**Overall: Blocked, not Completed.** Changes from the existing branch were preserved. No production database connection, merge or deployment was performed. Baseline: `240f65357c41e9930534d353236038111f3b795c`; frontend baseline: `c9f4bccd5449925de2428d1fa8428dd98196d1bf`.

## Acceptance checklist

| Item | Status | Evidence / limitation |
|---|---|---|
| Review current diff and retain previous changes | Passed | Existing zero-only changes retained; legacy Telegram source/tests unchanged. Final `git diff --check` clean. |
| Real Car.ir requests for different names | Passed | [Live query JSON](verification/carir-live-queries.json), results below. Market column only. |
| Parser against actual recorded Car.ir HTML | Passed | `RealRecordedCarIrTableSeparatesMarketFactoryAndUnknownAge`; real fixture under `CarShow.Tests/Fixtures`; [provenance](verification/source-provenance.json). |
| Original 10-ad message → exactly 10 ads / 8 zero | Blocked | Complete original fixture is absent from available inputs/repository. Six-part synthetic format coverage and three-part replay test do **not** satisfy this criterion. No substituted ten-ad sample was fabricated. |
| Extraction → reference → validation → comparison → DB, recorded HTTP | Passed | `RecordedCarIrExtractionValidationComparisonPersistenceAndReplay`; actual crawler/parser/service + isolated SQLite. Three listings, one outlier, two included, replay stores no extra listing. |
| Same pipeline with live Car.ir | Passed | [Live E2E evidence](verification/live-carir-controlled-e2e.json): controlled Telegram captions, real HTTP to Car.ir, local SQLite; 3 stored, 1 outlier, 2 sources, final 14 billion toman. Legacy tables empty. **Not live Telegram.** |
| Entire E2E with live Telegram | Blocked | [Telegram evidence](verification/live-telegram.json): all three public previews return `PreviewUnavailable`, zero scanned/stored. Not a successful E2E. |
| Multiple channels, repeated import, deduplication | Passed | `BulkChannelsDeduplicateAndInvalidIdentifierDoesNotDiscardOtherChannels`, `SuspiciousFirstQuoteDoesNotBlockOtherChannelsAndReplayIsIdempotent`, numbered-message replay and recorded E2E. |
| One failed channel must not stop others | Passed | `WorkerContinuesOtherChannelsAfterReportedOrThrownFailure(false/true)` runs the actual background worker: one failed run and two completed runs, two stored listings. |
| Baseline failures categorized | Passed | 12 failures reproduced on pristine baseline; final failing test-name set is identical. Detailed table below. |
| SQL Server model/snapshot and SQL batch checks | Passed | `MigrationConsistencyTests` and all four `SqlServerMigrationBatchTests`; included in 78/78 focused tests. |
| Migration on existing SQL Server test DB | Blocked | Official SQL Server 2022 16.0.4205.1 local engine failed during SQLPAL startup (`0xc0000142`, reason `0x2`) before accepting connections. [Startup evidence](verification/sqlserver-startup.log). No migration was executed. SQLite `EnsureCreated` and worker history seed are **not** migration tests. |
| Backend build | Passed | [Build log](verification/backend-build.log): zero errors; existing dependency warnings remain. |
| Focused zero-vehicle + migration tests | Passed | 78/78, zero skipped; [focused log](verification/zero-focused.log), [all outcomes](verification/test-results.json). |
| Full backend test suite | Failed | 168 total: 156 passed / 12 failed / zero skipped. Baseline: 145 total, 133 passed / 12 failed. [Final log](verification/backend-tests.log). |
| Frontend production build | Passed | [Build log](verification/frontend-build.log). |
| Frontend browser unit tests | Passed | Chrome Headless 131, 2/2; [test log](verification/frontend-tests.log). HTTP mocked; test-only Zone polyfills enable existing fakeAsync tests. |
| Deployed UI + backend browser E2E | Not Tested | No deployment performed. |
| Pull requests | Passed | Matching backend draft branch `fix/zero-carir-pipeline-20261102`; [frontend draft PR #42](https://github.com/amirhosseinmakineh/mayaCarFrontProduc/pull/42). Backend PR link is supplied in the delivery message. |

## Live market results

Fetched 2026-11-02 **20:24:12 Asia/Tehran**. All monetary values are **toman**. The first three source cells say approximately “one day ago”; `PriceAt` is therefore inferred as 2026-11-01 20:24:12 Tehran, **not an exact source publication timestamp**. Full UTC timestamps and source URLs are in the JSON.

| Query | Matched name | Market price | Price date | Status |
|---|---|---:|---|---|
| آیتو M5 مدل 2025 | آیتو M5 مدل 2025 | 14,000,000,000 | 2026-11-01, inferred | Matched |
| لاماکو فلو مدل 1404 | لاماکو فلو مدل 1404 | 4,500,000,000 | 2026-11-01, inferred | Matched |
| تانک 300 مدل 2025 | تانک 300 مدل 2025 | 16,500,000,000 | 2026-11-01, inferred | Matched |
| پژو 207 | None selected | — | — | Ambiguous / MultipleReferenceVariants |
| جنسیس جی80 2.5 توربو مدل 2026 | None selected | — | — | NotFound |

Recorded HTML additionally verifies that Aito's factory price **7,449,000,000** is never selected in place of its market price **14,000,000,000**, and a row with missing market price remains unavailable despite a factory price. Conservative matching does not resolve every configured generic name; ambiguity must not produce a fabricated reference.

## Baseline failure classification

Every case below belongs to the **legacy shared Telegram pipeline**, not the independent `ZeroVehicleService`. Some legacy scenarios contain new cars, but that does not make them tests of this separate zero-only service. These failures remain unresolved and were not deleted, skipped or weakened. Exact assertions are in [baseline failures](verification/baseline-failures.json) and [all outcomes](verification/test-results.json).

| Test (class abbreviated) | Observed failure / cause | Scope |
|---|---|---|
| TelegramReprocessing.RepairsLegacyInvalidCandidateInPlaceAndDoesNotReparseAgain | Expected `207`, actual canonical `پژو 207`; legacy name expectation differs from extractor. | Legacy, outside zero service |
| TelegramCollection.WorkerUsesCollectorPersistsMessagesAndDoesNotDuplicateOnRetry | Same canonical-name mismatch. | Legacy |
| TelegramPricing.MatchesCompanyModelTrimAndNormalizesMoney — toman | Expected Matched, actual Unmatched; fixture model `207` comparison identity differs from canonical brand/model identity. | Legacy |
| Same test — rial | Same identity mismatch, not demonstrated currency arithmetic failure. | Legacy |
| TelegramPricing.SeparatesUsedAndNewAndRejectsAmbiguousAndWrongTrim | Expected CarId 2, actual null; same old catalog/identity matching path. | Legacy |
| TelegramPricing.SixDistinctMessagesFromOneChannelRemainSixSamplesButOneIndependentSource | Expected 825m, actual 850m; existing calculator uses latest sample per channel. | Legacy |
| TelegramAllChannelPricing.LargeBatchUsesEveryChannelWithoutTenThousandRowTruncation | Expected 20,000 samples, actual 200; one latest sample per channel, not proven row truncation. | Legacy |
| TelegramPricing.RealPipelineAveragesDistinctObservationsAndRerunsWithoutDuplicatingHistory | Expected 900m, actual 908,333,333.33; legacy accepted amounts/latest-per-channel inputs differ from older expectation. | Legacy |
| TelegramPricing.EmptyDataKeepsPreviousPriceAndLateMessageDoesNotReopenFinalDay | Expected Late, actual Matched; finalization/window/status behavior mismatch remains unresolved. | Legacy |
| TelegramPricing.PublicationDateWinsOverReceiptAndFinalDayCannotBeReopened | Expected seven Late records, actual zero; same finalization/status path. | Legacy |
| TelegramReprocessing.ActualDenaMessageWithoutPriceDoesNotTreatDeliveryDaysAsMoney | Expected candidate, actual null; ambiguous known-name text and no price reaches extractor's no-name/no-price return. | Legacy |
| TelegramCollection.WorkerDrains200JobsIncludingAnInaccessibleChannel | Baseline full run and isolated rerun hit fixture's 60-second cancellation. Final branch reaches assertion: expected zero listings, actual 199; legacy extractor retains price-only review candidates. Timing and old expectation are separate limitations. | Legacy |

There are **zero failing independent zero-service tests** in the final run. Existing production behavior in the legacy pipeline still needs a separate task; passing focused tests does not make the full suite green.

## Migration review and remaining execution

`20261102050507_ZeroVehicleCarIrPipeline` changes only ZeroVehicle tables. Existing `(Channel, ExternalMessageId)` uniqueness implies uniqueness after adding `PartIndex=0`; the new index allows distinct parts. Nullable reference fields preserve existing rows. Old price groups default to `PricingStatus=Unavailable` and `MarketPrice=0`, preventing them from being presented as verified Car.ir prices. No table/data drop occurs in Up. SQL generation separates new columns from dependent index batches. The [idempotent SQL script](zero-vehicle-carir-migration.sql) is provided.

This is **static compatibility review**, not proof against a production backup or existing SQL Server data. Before approval: on an isolated SQL Server test database at the previous migration, seed representative old zero listings/groups and unrelated legacy rows, apply Up, verify preserved rows/defaults/new index, repeat the idempotent script, and verify no unintended legacy changes. Do not use production credentials. Down recreates the old two-column unique index and may fail once multipart messages exist; rollback with such data is untested and must not be assumed safe.

## Narrow fix found by testing

`NumberedMessagesPersistEveryZeroPartAndRejectPositiveMileageIndividually` exposed a genuine bug: `بدون کارکرد ۲۹۵۰م` treated the following price as mileage. Mileage scanning now removes the zero-condition phrase before checking odometer expressions. Explicit `۱۶ هزار کیلومتر` and another positive mileage claim still override zero; two additional regressions verify that. No healthy legacy processing was rewritten.

## Reproduction

Use .NET 10 and installed frontend dependencies. Commands run from backend root unless stated otherwise:

```sh
dotnet build CarShow/CarShow.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test CarShow.Tests/CarShow.Tests.csproj -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test CarShow.Tests/CarShow.Tests.csproj --filter 'FullyQualifiedName~CarShow.Tests.Zero|FullyQualifiedName~MigrationConsistency|FullyQualifiedName~SqlServerMigrationBatch'
dotnet build tools/ZeroVehicleProbe/ZeroVehicleProbe.csproj
dotnet run --no-build --project tools/ZeroVehicleProbe -- --market-only '--name=آیتو M5 مدل 2025' '--name=لاماکو فلو مدل 1404' '--name=تانک 300 مدل 2025'
dotnet run --no-build --project tools/ZeroVehicleProbe -- --live-e2e
dotnet run --no-build --project tools/ZeroVehicleProbe -- @aliajamihavaleh @baravard @foroshekhodro
# Frontend repository:
NG_BUILD_MAX_WORKERS=1 npm run build
CHROME_BIN=/path/to/chrome NG_BUILD_MAX_WORKERS=1 npm test -- --watch=false --browsers=ChromeHeadless '--include=**/zero-vehicles.component.spec.ts'
```

`--live-e2e` intentionally asserts the observed 14-billion reference; a future source price change can fail that dated acceptance sample and must be inspected, not silently blessed. Probe databases are unique temporary SQLite files; production connection strings are never used.
