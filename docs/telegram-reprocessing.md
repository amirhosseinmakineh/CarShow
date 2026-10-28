# Telegram extraction replay and admin contract

Extraction version 2 replays raw messages in bounded batches (default 2000 per pricing cycle). The pricing worker selects old-version raw rows and newly extracted candidates. It updates derived, unpublished candidates in place using channel/message uniqueness; raw text, original links, published cars and existing price history remain intact. Version markers prevent repeated parsing on restart and prevent ignored messages from starving the queue.

Legacy non-ad rows remain auditable with `Ignored` / `NON_VEHICLE_MESSAGE`; the default observation list excludes them, and `status=Ignored` exposes them explicitly. A new non-ad message only needs its raw record. Candidate, ignored-message and valid-price counts come from persisted records rather than cumulative extraction counters.

Explicit prices are normalized independently of vehicle condition. Missing currency, missing price, unknown condition, identity gaps, image/source gaps and insufficient independent channels have separate reason codes. The paired Angular admin maps these reasons to Persian and uses cards on mobile so reasons are not clipped by a wide table. News text after `خودرو:` is not accepted as an arbitrary long model name.

Comparison for daily history uses the same identity and Tehran publication day. The source channel plus five other distinct channels are required; one newest observation per channel enters a daily mean. Telegram-derived cars with no valid daily price are not returned as published inventory. Comparison assignments are persisted before calculating the day's price, including the first successful cycle with an empty catalog.

## Migration and rollout

`20261028044523_TrackTelegramExtractionVersion` only adds integer version markers with default 0 to raw messages and candidates. It does not delete rows or reset tables. Deploy backend first, allow bounded replay to complete, then deploy the paired frontend branch/PR. Use a restorable SQL Server backup before applying any production migration.

Rollback uses the previous application image and keeps additive columns and all raw rows. No schema downgrade is required or performed. Raw messages can be replayed by a future higher extraction version.

## Validation

- 47 backend tests passed, including legacy candidate repair, news retention/exclusion, no replay duplicates, published-row preservation, missing-price real-message fixture, price/condition separation, Unicode/thousands units and six-channel car/history upsert.
- SQL Server model matches the latest generated migration snapshot; incremental SQL contains no destructive operation.
- Angular production build and spec TypeScript compilation passed. Local Chrome test execution was blocked by sandbox Unix-socket restrictions, so runtime UI test success is not claimed.
- Unit/integration fixtures do not establish six real comparable Telegram sources; production counts must be measured separately after deployment.
