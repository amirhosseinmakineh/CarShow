# All-channel daily market pricing

## Rule and pipeline

The five-other-channel rule is replaced by all registered, non-deleted channel IDs. Pausing a channel stops new collection; existing valid observations in the price window remain available. Failed or empty channels do not contribute invented values or block other channels.

Collection stores raw messages and extracted candidates as before. After validation, candidates are grouped by exact normalized manufacturer/model/trim/year/calendar/condition identity and Tehran publication day. The existing image, source link, explicit currency, positive price, condition and used-mileage requirements remain in force. Incomplete messages retain their reason and never enter the mean.

For each identity/day:

1. Collapse copied advertisements by fingerprint for that day.
2. Select the latest unique valid observation per channel (publication timestamp, then persisted ID as deterministic tie-breaker).
3. Compute the ordinary arithmetic mean in normalized toman using decimal arithmetic, rounded to two places away from zero.
4. Upsert a vehicle even if there is only one complete source. Report the actual source count; do not imply multiple-source corroboration when it is one.
5. Persist all comparison assignments before calculating the daily history. The same vehicle/day has one history row, updated idempotently while its window is open. Finalized days remain unchanged.
6. Return the stored price through the existing public new/used inventory APIs. Public visibility requires a positive stored price and at least one valid channel sample, rather than six.

All observations in a comparison group receive the same comparison average. Only selected latest samples have `ComparisonStatus=Compared` and `ProcessingStatus=Published`. Earlier valid samples from the same channel are retained as `Superseded` / `Stored` with `NEWER_CHANNEL_SAMPLE`; copied ads remain `Rejected` / `DUPLICATE_AD`. Raw text and original source/media links are preserved.

A daily price can change as valid sources arrive. It is a stored daily estimate, not a guarantee that a seller will transact at that price. No median, outlier filter or undisclosed weighting is applied.

## Architecture and performance

- Application layer: `TelegramMarketPriceCalculator` contains pure validation and comparison rules; `TelegramVehicleCatalog` handles per-cycle cached catalog upserts through the Domain store interface; `TelegramPricingService` orchestrates them and writes daily history.
- SQL Server Infrastructure, repository contracts and the existing exclusive transaction/application lock remain authoritative. No direct database dependency is added to the application layer.
- The old global `Take(10000)` and `Take(5)` sampling caps are removed. Pricing reads the open window and newly expired published observations, rather than repeatedly scanning the complete raw archive.
- Vehicle/company/model/trim records are loaded once per cycle and reused through dictionaries. Generated IDs are assigned with one catalog flush for all groups; there is no query/save round trip per vehicle.
- Raw status updates load IDs in batches of 1000. Daily cars/history use dictionary lookups instead of scans for every group.
- Existing bounded extraction replay (default 2000 per cycle) remains; the pricing worker interval remains configurable, default 60 seconds. All registered channels means their stored comparable data, not a new network request per car on every calculation.

## API and frontend

The existing admin observation contract adds optional `registeredChannelCount`. `validPriceSourceCount` is the number of selected channel samples including the source; `comparedChannelCount` is the other selected channels. It is not a required-channel counter.

Angular shows actual contributing channels out of registered channels, explains a single-source result, labels superseded observations, and removes the five-channel progress requirement. Public inventory and its existing new/used filters continue using the stored daily market price.

## Validation and deployment

Tests cover first-cycle publication with 1/2/8/20 sources, a new source updating the same history, source count/average consistency, copied advertisements, latest-per-channel weighting, new/used/year/trim/day separation, invalid/deleted-source exclusion, restart/idempotence, public inventory DTOs and a 20000-observation/200-channel comparison case. Synthetic fixtures are explicitly not live Telegram evidence.

This change has no entity or schema changes and requires no new migration. The SQL Server snapshot consistency test must pass. Deploy the backend before the paired frontend; the additional DTO property is backward compatible. Keep the previous images for rollback. No table resets, data deletions or schema downgrades are part of rollout or rollback.

### Verified on this feature branch

- Backend Release build and all 57 tests passed, including SQL Server snapshot consistency and the public new/used inventory contract.
- Pure comparison diagnostic: 20000 observations / 200 channels in 220 ms on this development runner. This is not a live collection/database benchmark or production latency guarantee.
- Angular production build and spec TypeScript compilation passed. Browser-based Karma execution remains unverified because the development browser runner is restricted.
- Production Telegram data and production database rows were not changed by this feature validation. Real-data publication must be checked after an approved rollout; missing identity or condition remains a genuine blocker.
