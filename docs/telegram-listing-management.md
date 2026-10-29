# Telegram listings and job timeline

The admin-only `DELETE /api/admin/telegram-pricing/observations` accepts
`{"expectedCount": 123}`. The count must match the current number of active
non-ignored candidates. A mismatch or a competing pricing run returns 409.
The operation soft-deletes those candidates and marks their linked raw messages
as cleared in one transaction. Raw messages and daily price histories remain
for audit. A later extraction-version upgrade does not reactivate the cleared
candidates; fresh Telegram messages can produce new listings. The site hides a
Telegram-derived car when it has no active published listing.

The default observations endpoint returns only matched, compared, published
listings with a positive normalized price, source link, image, company, model,
trim, year and vehicle condition. Admin callers can still request a specific
status for diagnostics; the admin listing page uses the healthy default.

`GET /api/admin/telegram-pricing/jobs/{id}/timeline` returns collection job
timestamps and stages derived from the job's channel and the collection window.
The raw message and candidate timestamps are retained after a clear. Pricing
may complete later than collection; stages without recorded data remain waiting
or show no data. A job whose collection failed shows a failed collection stage.
The existing schema has no explicit job-to-message relationship, so an older
job that deduplicated every message has no associated newly collected messages
in its timeline. This endpoint does not invent those counts.

No schema migration is required. Before publishing, back up the SQL Server
database and deploy backend before frontend. Roll back both application
versions to their previous commits if needed. No data is physically deleted;
restoring visibility of a deliberately cleared candidate requires a separate
reviewed data recovery operation.
