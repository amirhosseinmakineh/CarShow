# Final zero-only acceptance report — updated 2026-11-03

**Overall: Blocked / Incomplete. No merge, deployment or production database access.**

Continued backend PR #32 at `300ac030376c4a281e2481247f331684449dd838` and frontend PR #42 at `b96642e8e3b6a2239058dcb71ca4e02270cd4c59`. Both remote heads matched the local branches before changes. Backend had only untracked previous test output; frontend had no uncommitted changes. Previous changes and migration are retained. Evidence timestamps below are from the 2026-11-02 execution; the report was finalized on 2026-11-03.

## Acceptance checklist

| Section | Status | Reviewable evidence |
|---|---|---|
| Exact ten-ad message | Passed | [Original fixture](../CarShow.Tests/Fixtures/ten-vehicle-original.txt), [provenance/hash](verification-final/fixture-provenance.json), [actual extraction](verification-final/ten-extraction.json): 10 parts, 8 zero, 2 nonzero |
| Eight names and amounts | Passed | Table below; correct zero indices 1,3,4,5,6,7,8,9. Decimal shorthand is a candidate, not an approved price. |
| Car.ir parser, cache and failure handling | Passed | Real recorded HTML test, live result/cache equality; timeout, HTTP/network, retry, stale and rate-limit tests in [outcomes](verification-final/test-results.json). No factory fallback. |
| Usable matching coverage of 20 names | Failed | All 20 queries executed, but only 3 Matched (15%); 16 Ambiguous and 1 NotFound. Full live table below. Not claimed as 20 successful matches. |
| Actual extracted names → usable Car.ir price | Failed | [Live exact-name lookup](verification-final/carir-extracted-names-live.json): 7 unique names from 8 zero ads, 0 Matched; 5 Ambiguous and 2 NotFound. No reference price was invented. |
| Three live Telegram channels | Blocked | [Transport/HTML evidence](verification-final/telegram-network.json), [collector runs](verification-final/live-telegram.json): identical HTTP 200 Site Unavailable pages; zero messages from each |
| Collector with real caption in valid constructed HTML | Passed | Actual Collector + zero service + SQLite; HTML envelope and timestamp/message ID are controlled test metadata, not live Telegram. [Persistence/replay output](verification-final/ten-persistence-replay.json) |
| Repeat import / identity / cursor | Passed | First import 8 stored; second 0 stored / 8 duplicates; message ID 901, part indices 0,2,3,4,5,6,7,8; latest message 901 preserved |
| Multichannel failure isolation / pagination | Passed | Actual worker handles both returned failure and thrown exception then completes the other two channels; existing 500-message/25-page collector test passed |
| Pricing A–H | Passed | 11 controlled-reference test cases, including four unavailable statuses; [inputs/expected/actual outputs](verification-final/pricing-scenarios.json) |
| Recorded HTML E2E | Passed | Existing recorded Car.ir extraction→validation→comparison→SQLite→replay test reran successfully |
| Live Car.ir + controlled Telegram E2E | Passed | [Evidence](verification-final/live-e2e.json): 3 stored, 1 outlier, 2 sources, final 14 billion toman; no legacy rows |
| Fully live Telegram→Car.ir→SQL Server E2E | Blocked | Telegram previews unavailable and SQL Server cannot start. Other test types do not replace this test. |
| SQL Server migration execution | Blocked | [Actual engine attempt](verification-final/sqlserver-startup.log): SQLPAL `0xc0000142`, reason `0x2`, engine exit 1 before accepting connections |
| SQL snapshot/batches and migration review | Passed | Same migration retained; model consistency and SQL batch tests passed. Regenerated script/check recorded separately. Not an executed migration. |
| Previous 78 tests | Passed | All 78 test names are present and Passed in new focused results |
| Focused tests including pagination | Passed | 107 passed / 0 failed / 0 skipped; [log](verification-final/focused.log) |
| Full backend tests | Failed | 192 total: 180 passed / 12 legacy failures / 0 skipped; [log](verification-final/backend-tests.log), [machine comparison](verification-final/test-results.json) |
| Backend build | Passed | [Build log](verification-final/backend-build.log) |
| Frontend build | Passed | [Build log](verification-final/frontend-build.log) |
| Admin browser tests | Passed | Chrome Headless 131: 4/4; includes grouped IDs and bulk-import error visibility. HTTP mocked. [Log](verification-final/frontend-tests.log) |
| Deployed UI E2E | Not Tested | No deployment requested/performed |

## Exact original fixture — actual extraction

The original caption retains the header, all ten ads, mixed keycaps including `1️⃣0️⃣`, line breaks, trailing spaces and both phone numbers. Row 2 is rejected as `POSITIVE_MILEAGE` (16 thousand); row 10 is rejected for 11 thousand. The generic zero heading is outside all ad bodies. Phone numbers do not become extra listings or prices; 24m ancillary card cost is excluded.

Amounts below are **toman candidates**. A candidate only enters pricing if exactly one interpretation fits a valid Car.ir reference within tolerance. Without a matched reference, stored rows remain `Unavailable` with `FinalPrice=null`; this was asserted for all eight through the Collector/database test. Raw-name text is a verbatim excerpt of each raw caption, not an extra inferred trim.

| Row | Raw name excerpt | Standard name | Raw amount | Normalized candidate (toman) | Extraction |
|---|---|---|---|---:|---|
| 1 | پژو ۲۰۷ هیدرولیک | پژو 207 دنده‌ای هیدرولیک TU5P | ۲۸۸۰م | 2,880,000,000 | Passed; candidate only |
| 3 | پژو تیوتری | پژو 207 TU3 | ۲۸۴۵م | 2,845,000,000 | Passed; candidate only |
| 4 | پژو ۲۰۷ تیوتری | پژو 207 TU3 | ۲۸۸۰م | 2,880,000,000 | Passed; candidate only |
| 5 | وانت پراید ۱۵۱ | پراید 151 | ۱.۷م | 1,700,000,000 | Passed; candidate only |
| 6 | نیسان پادرا پلاس | پادرا پلاس | ۲.۶م | 2,600,000,000 | Passed; candidate only |
| 7 | اطلس جی ال | اطلس | ۲۰۴۵م | 2,045,000,000 | Passed; candidate only |
| 8 | تارا توربو اتومات | تارا | ۴۴۹۰م | 4,490,000,000 | Passed; candidate only |
| 9 | لاماری اکو‌ | لاماری اکو | ۶.۲م | 6,200,000,000 | Passed; candidate only |

## Twenty live Car.ir queries

One real public price page supplies all rows; cache reuse does not mean twenty separate HTTP downloads. Each query was immediately repeated against cache and the full result objects matched. Recorded-HTML concurrent tests separately prove one price-page request for twenty callers. Live source age was approximately **two days**, yielding inferred `PriceAt=2026-10-31T18:11:35Z` (21:41 Tehran); this is **not today's price or an exact publication timestamp**. Configured maximum age is seven days. Unknown/older-than-limit dates return Stale in controlled tests.

| Query | Status | Matched name | Market price (toman) | PriceAt UTC (inferred) | Source URL |
|---|---|---|---:|---|---|
| آیتو M5 مدل 2025 | Matched | آیتو M5 مدل 2025 | 14,000,000,000 | 2026-10-31T18:11:35.8783565Z | https://car.ir/1017-eres-hm5-erev |
| لاماکو فلو مدل 1404 | Matched | لاماکو فلو مدل 1404 | 4,500,000,000 | 2026-10-31T18:11:35.8783565Z | https://car.ir/1027-lamaco-flo-at |
| تانک 300 مدل 2025 | Matched | تانک 300 مدل 2025 | 16,500,000,000 | 2026-10-31T18:11:35.8783565Z | https://car.ir/973-tank-300-automatic |
| پژو 207 | Ambiguous | — | — | — | — |
| شاهین | Ambiguous | — | — | — | — |
| تارا | Ambiguous | — | — | — | — |
| دنا پلاس | Ambiguous | — | — | — | — |
| KMC J7 | Ambiguous | — | — | — | — |
| اطلس | Ambiguous | — | — | — | — |
| لاماری اکو | NotFound | — | — | — | — |
| لاماری ایما | Ambiguous | — | — | — | — |
| پراید 151 | Ambiguous | — | — | — | — |
| پادرا پلاس | Ambiguous | — | — | — | — |
| کوییک | Ambiguous | — | — | — | — |
| ساینا | Ambiguous | — | — | — | — |
| سهند | Ambiguous | — | — | — | — |
| ری را | Ambiguous | — | — | — | — |
| رانا پلاس | Ambiguous | — | — | — | — |
| سورن پلاس | Ambiguous | — | — | — | — |
| هایما S7 | Ambiguous | — | — | — | — |

[Full live result and cached result](verification-final/carir-20-live.json). Live status totals: Matched=3, Ambiguous=16, NotFound=1, Stale=0, SourceUnavailable=0. The absence of a live stale/network case is not filled with invented live data: Stale and source error behavior were exercised with explicitly controlled inputs.

Why coverage is low: generic vehicle names hit several rows/years/variants; selecting one arbitrarily would violate the task. `لاماری اکو` has no matching row under that spelling on the fetched page. Exact extracted `پژو 207 دنده‌ای هیدرولیک TU5P` also has no all-token match. This is an unresolved matching limitation, not proof that those vehicles lack market prices. No complex year/trim expansion or price-proximity selection was added.

Exact extracted-name live results:

| Extracted standard name | Result |
|---|---|
| پژو 207 دنده‌ای هیدرولیک TU5P | NotFound |
| پژو 207 TU3 | Ambiguous |
| پراید 151 | Ambiguous |
| پادرا پلاس | Ambiguous |
| اطلس | Ambiguous |
| تارا | Ambiguous |
| لاماری اکو | NotFound |

## Pricing scenarios — controlled references, not live prices

Reference for every case below is a controlled 3,000,000,000 toman. Default tolerance is 15%. Final price is `(reference + sum(valid independent quotes)) / (1 + valid quote count)`. These outputs come from real service execution against isolated SQLite.

| Scenario | Input captions | Market status | Expected final | Actual final | Samples | Result |
|---|---|---|---:|---:|---:|---|
| A | تارا صفر ۲۹۵۰م | Matched | 2,975,000,000.00 | 2,975,000,000.00 | 1 | Passed |
| B | تارا صفر ۱۲۰۰م | Matched | 3,000,000,000.00 | 3,000,000,000.00 | 0 | Passed |
| C | تارا صفر ۴۰۰۰م | Matched | 3,000,000,000.00 | 3,000,000,000.00 | 0 | Passed |
| D | تارا صفر ۱۲۰۰م | Matched | 3,000,000,000.00 | 3,000,000,000.00 | 0 | Passed |
| E | تارا صفر ۲۹۵۰م | Ambiguous | null | null | 0 | Passed |
| E | تارا صفر ۲۹۵۰م | NotFound | null | null | 0 | Passed |
| E | تارا صفر ۲۹۵۰م | SourceUnavailable | null | null | 0 | Passed |
| E | تارا صفر ۲۹۵۰م | Stale | null | null | 0 | Passed |
| F | تارا صفر ۲۹۰۰م تهران / تارا صفر ۳۱۰۰م مشهد / تارا صفر ۳۰۰۰م شیراز | Matched | 3,000,000,000.00 | 3,000,000,000.00 | 3 | Passed |
| G | تارا صفر ۲۹۰۰م / تارا صفر ۲۹۰۰م / تارا صفر ۲۹۰۰م | Matched | 2,950,000,000.00 | 2,950,000,000.00 | 1 | Passed |
| H | تارا بدون کارکرد ۲۹۵۰م؛ کارت طلایی ۲۴م تسویه شد | Matched | 2,975,000,000.00 | 2,975,000,000.00 | 1 | Passed |

A includes a near quote; B rejects an extremely low quote while other-channel tests prove continued processing; C rejects a high quote; D yields `ReferenceOnly`; E yields `Unavailable` and null final for NotFound/Ambiguous/Stale/SourceUnavailable; F averages three channels plus reference; G counts identical reposted content only once; H separates price from the zero-condition phrase and the 24m card cost.

## Telegram diagnosis and live reproduction

All three ordinary HTTPS requests reached an HTTP endpoint and received **200**, identical 195-byte HTML titled `Site Unavailable`, with no `tgme_channel_history`. The body hash is identical across channels. Thus this is not a demonstrated DNS/connection timeout and not a failure to parse genuine message HTML. The responsible intermediary/origin cannot be identified conclusively from this response alone. The collector correctly reports `PreviewUnavailable`; a separate controlled HttpRequestException produces `NetworkError`. The shared legacy collector was not modified. No alternate route, credentials or restriction bypass was attempted.

Live channels: `aliajamihavaleh`, `baravard`, `foroshekhodro`; each scanned/stored **0**. Actual unavailable response is saved in Fixtures and tested. The valid-message test uses the exact real caption in a **constructed valid Telegram envelope**, explicitly not a captured live page.

On a machine with normal access, run from backend root (PowerShell; public previews only):

```powershell
dotnet build tools/ZeroVehicleProbe/ZeroVehicleProbe.csproj
New-Item -ItemType Directory -Force ./local-verification | Out-Null
curl.exe -L -D ./local-verification/telegram.headers https://t.me/s/baravard -o ./local-verification/telegram.html
dotnet run --no-build --project tools/ZeroVehicleProbe -- --report=local-verification/live-telegram.json '@aliajamihavaleh' '@baravard' '@foroshekhodro' '@aliajamihavaleh' '@baravard' '@foroshekhodro'
```

The probe uses a unique temporary SQLite DB, never production connection settings. It currently requests up to 100 messages/channel. The repeated channel arguments exercise replay in the same DB; inspect each run's Scanned/Extracted/Stored/Duplicates and any newly published messages. Record the JSON and exit code. A run with zero scanned messages is not successful Live acceptance. Test 500-message pagination separately with `TelegramPreviewTests.PaginatesTo500WithoutDuplicatesAndReturnsResumeCursor` (fixture, not live). To prove SQL Server E2E, run the same application import flow against the isolated migrated SQL Server test instance after the next section succeeds.

## SQL Server migration: review and blocked execution

Migration `20261102050507_ZeroVehicleCarIrPipeline` is unchanged. Up only changes ZeroVehicle tables, adds nullable/defaulted fields and replaces the two-column uniqueness with `(Channel, ExternalMessageId, PartIndex)`. Old rows default to PartIndex=0; previous uniqueness prevents new collisions. Old price groups default to Unavailable. No Up data/table drop was introduced. Down can fail when multipart rows would violate the old unique index; rollback with such rows is not verified.

This attempt downloaded official SQL Server 2022 `16.0.4205.1-1`, supplied required LDAP/SSS libraries, configured Developer edition, port 14333, loopback address, isolated test data paths and 2048MB memory limit. The engine failed in SQLPAL before accepting connections (exit 1). Password was temporary and not logged/committed. The previous attempt has the same failure. Runtime failure is an environmental blocker, not evidence that the migration is correct or incorrect.

The following remain **Blocked**: applying Up to an existing SQL Server test DB, post-migration table/index/constraint inspection, real SQL Server insert/query/reimport, preservation checks and transaction-log growth measurement. SQLite EnsureCreated and a manually seeded worker migration gate are not substitutes.

Use a fresh local/non-production instance and a new test database. Example commands require an explicit test connection string; do not load production settings:

```powershell
# Set only to localhost test SQL Server, database CarShowZeroTest_20261103.
# Supply a test password locally; never paste production credentials.
$TestConnection = $env:CARSHOW_ZERO_TEST_CONNECTION
if (!$TestConnection -or $TestConnection -notmatch 'CarShowZeroTest_20261103') { throw 'Explicit isolated test connection required' }
dotnet ef database update 20261030213449_IndependentZeroVehicles --project CarShow.Infrastracture --startup-project CarShow --connection $TestConnection
# Seed representative old zero listings/groups and unrelated legacy rows; record row IDs/counts.
dotnet ef database update 20261102050507_ZeroVehicleCarIrPipeline --project CarShow.Infrastracture --startup-project CarShow --connection $TestConnection
# Repeat: must report database already up to date.
dotnet ef database update 20261102050507_ZeroVehicleCarIrPipeline --project CarShow.Infrastracture --startup-project CarShow --connection $TestConnection
```

Before/after, save: `SELECT * FROM __EFMigrationsHistory`; `sys.columns`, `sys.indexes`, `sys.index_columns`, `sys.foreign_keys` for ZeroVehicle tables; unchanged row counts/IDs in legacy tables. Verify one old listing retains its values/PartIndex=0, then import the exact fixture twice and verify 8 then 0 new rows, correct composite uniqueness and null final for unresolved references. Measure `SELECT * FROM sys.dm_db_log_space_usage` before and after (plus file sizes from sys.database_files); do not call normal log use a leak without repeat measurements. Inspect `sys.columns`/constraints for Cars/CarListings before and after to prove unchanged schema. These steps are instructions, **not executed results**.

## Regression and scope

All previous 78 focused test names passed again; 107 focused tests now pass including five existing TelegramPreview cases. Full suite: 180/192 passed, exactly the same 12 failing test names as baseline, zero skipped. [Machine-readable evidence](verification-final/test-results.json) includes each result. Individual classification of the twelve unchanged legacy failures remains in [previous report](zero-vehicle-verification.md#baseline-failure-classification): five identity/name cases, three aggregation/sample expectations, two finalization/status cases, one no-price ambiguous-name case, and one worker drain/assertion case. They remain Failed; no legacy tests were removed, skipped or weakened.

The initial acceptance run exposed real missing mileage/name handling. Corrections are limited to zero rules and zero catalog: `هزارتاکار`, `پژو تیوتری`/TU3 aliases, پراید 151 and پادرا پلاس, and Arabic decimal shorthand parity. Test-only worker fixture now uses separate SQLite connections with WAL; concurrent DbContexts sharing one connection caused SQLite Error 5 in the first full run. Assertions were retained, and the final full run passed both worker cases. This fixture issue is recorded rather than hidden.

Frontend changes in this continuation are tests/report only: grouped identifier submission, per-channel error display and bulk-import busy-state release. No unnecessary UI rewrite. Backend legacy/shared Telegram source and legacy tests have no changes in this continuation. The exact fixture intentionally retains trailing whitespace; whitespace checks exclude that one input file.

PRs: [backend #32](https://github.com/amirhosseinmakineh/CarShow/pull/32), [frontend #42](https://github.com/amirhosseinmakineh/mayaCarFrontProduc/pull/42). Both remain Draft; no merge/deploy.
