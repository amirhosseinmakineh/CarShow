# Telegram channel collection

`/api/admin/telegram-channels` is an Admin-only API for normalized channel registration, bulk import, queueing, status, pause/resume, retry and deletion. Jobs are processed by a bounded single worker queue and persisted in SQL Server. Unique indexes prevent duplicate channels and raw messages.

The deployment currently has no Telegram MTProto adapter. A Bot Token cannot read arbitrary public channels; therefore jobs report `AccessRequired` and never claim success. Real collection requires `Telegram:ApiId`, `Telegram:ApiHash` and an authorized MTProto session/provider to be configured in backend secret configuration, followed by implementing the approved `ITelegramCollector` adapter. Tokens are never sent to the frontend.

## Real MTProto configuration

Set these values only in secret configuration or environment variables:

```text
Telegram__ApiId
Telegram__ApiHash
Telegram__PhoneNumber
Telegram__SessionPath
```

The account must be authorized and have access to each channel. The collector reads up to 500 recent messages per queued channel, stores each message idempotently, then extracts listings with a replaceable `ITelegramListingExtractor`. Messages without a positive price are intentionally excluded from listings.

The backend also requires `ConnectionStrings__CarShow` and `JwtSettings__SecretKey` in the deployment environment. These values are intentionally absent from source-controlled appsettings files.

Optional tuning values are `Telegram__HistoryPerChannel` (default 500, clamped to 1..2000) and the worker currently processes one queued channel at a time to keep Telegram rate limits bounded.
