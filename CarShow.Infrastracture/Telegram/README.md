# Telegram car scan API

`GET /api/telegram-cars/scan?maxMessagesPerChannel=500` calls the provided `TelegramCarService.GetCarsAsync` unchanged and returns its `List<TelegramCar>` (only `Name`, `Year`, `Price` in toman). It requires an Admin JWT; Swagger has a Bearer authorization control. The service scans all 84 public usernames in the exact order of the supplied array. Progress/errors from its optional `log` callback go to the application log. It is an on-demand HTTP endpoint, with no database writes, migration or scheduler.

A complete scan may take much longer than an HTTP proxy timeout: there is at least an 800 ms delay after each of the 84 channels. Use a small `maxMessagesPerChannel` for testing, but the per-channel delay still applies. The `t.me/+...` private invite was not in the supplied code and is not supported by Telegram's public `/s/` pages.

Local parser checks: `dotnet run --project tests/TelegramCarScanner.Tests/TelegramCarScanner.Tests.csproj`.
