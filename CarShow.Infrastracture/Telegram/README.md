# Telegram car scan (on demand)

`GET /api/telegram-cars/scan?channel=autopalermo&maxMessagesPerChannel=20`
requires an Admin JWT. Omit `channel` to scan all 84 approved public usernames (at most eight HTTP requests concurrently). The response contains `cars` with `name`, `year`, `price` in toman, `channel`, `messageId`, `messageUrl`, plus per-channel `channels` counts and errors. The private invite is intentionally excluded: the public `t.me/s/` page cannot read it. This operation does not persist cars, write to the database, change the existing crawler, or run in the background.

For a quick isolated check, use one channel and a small limit. All-channel scans can take longer than a gateway timeout; results are returned only when the request finishes. Telegram can return HTTP 200 with an unavailable page, which is reported in `error` rather than as zero cars.

Local checks: `dotnet run --project tests/TelegramCarScanner.Tests/TelegramCarScanner.Tests.csproj`. Pass `-- --live` for an optional public Telegram fetch. The live check is network-dependent.
