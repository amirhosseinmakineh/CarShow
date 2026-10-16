# Telegram channel collection

`/api/admin/telegram-channels` is an Admin-only API for normalized channel registration, bulk import, queueing, status, pause/resume, retry and deletion. Jobs are processed by a bounded single worker queue and persisted in SQL Server. Unique indexes prevent duplicate channels and raw messages.

The deployment currently has no Telegram MTProto adapter. A Bot Token cannot read arbitrary public channels; therefore jobs report `AccessRequired` and never claim success. Real collection requires `Telegram:ApiId`, `Telegram:ApiHash` and an authorized MTProto session/provider to be configured in backend secret configuration, followed by implementing the approved `ITelegramCollector` adapter. Tokens are never sent to the frontend.
