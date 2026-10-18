# Telegram channel collection

The Admin API is `/api/admin/telegram-channels`. Every endpoint requires a valid JWT with the `Admin` role. The dashboard uses this prefix for channel lists, `summary`, `jobs`, `capabilities`, `import`, enabled changes, retry, test and delete.

## Public channels without Telegram credentials

Use `Telegram__Provider=auto` (default) or `Telegram__Provider=web-preview`. When API credentials are absent, `auto` reads `https://t.me/s/{username}`. There is no need for a bot token, bot administrator rights, API ID, API hash, phone number or session in this mode. The backend host must be able to reach Telegram over HTTPS.

Only public usernames with an accessible preview are supported. Private channels, numeric peer IDs and unavailable previews produce access errors. The preview exposes recent visible text messages, not guaranteed complete history. A successful HTTP response containing a login/blocked/unavailable page or no readable messages is never reported as successful collection.

Optional MTProto mode uses WTelegramClient and requires backend-only `Telegram__ApiId`, `Telegram__ApiHash`, `Telegram__PhoneNumber` and a previously authorized `Telegram__SessionPath`. The capability endpoint describes the selected provider; actual connection/access is checked by a job.

## Local configuration

Set `ConnectionStrings__CarShow` to a reachable SQL Server database and set `JwtSettings__SecretKey` to a private signing key of at least 32 UTF-8 bytes for HS256. Keep the issuer/audience consistent with existing login configuration. Do not commit credentials or send them to the frontend.

```bash
dotnet restore CarShow.slnx
dotnet build CarShow.slnx --no-restore
dotnet run --project CarShow
```

The existing `Program.cs` startup applies EF migrations and idempotent SQL provisioning for Telegram tables. This fix does not introduce a new schema or replace that existing provisioning mechanism. SQL execution still needs verification against your SQL Server instance.

## Queue and persistence

Import normalizes usernames/URLs, counts existing and within-request duplicates, saves channels and persists `Queued` job rows before waking the worker. One worker performs collection serially. SQL rows remain the source of truth; in-memory notifications do not contain messages. Duplicate wake-ups cannot create another attempt without a queued SQL job. Run a single application/worker instance; this is not a distributed claim/lease queue.

On restart, interrupted jobs are marked failed and enabled Pending/Processing channels are queued again. Pause persists and is preserved if it occurs while collection is awaiting the network. Resume/manual retry resets the retry budget. A test action enables and queues the channel, just like a manual retry. Deleted channels are skipped.

Transient errors and unexpected collector/processing exceptions receive exponential backoff. Access errors are not automatically retried. Limits:

| Setting | Default | Allowed range |
| --- | --- | --- |
| `Telegram__MaxRetries` | 3 | 0–10 |
| `Telegram__TimeoutSeconds` | 120 | 1–600 seconds |
| `Telegram__RequestDelayMilliseconds` | 1000 | 100–60000 ms |
| `Telegram__HistoryPerChannel` | 500 | 1–2000, MTProto request limit |

The worker transaction saves raw messages, parsed listings, counts and completion state together. Unique constraints enforce channel/message identity. The extractor supports explicit price, mileage and year fields, Persian/Arabic digits, thousand/million/billion multipliers and explicit toman/rial currency. Missing currency remains unknown; it is not guessed. Model is extracted only from an explicit car/name field. Other optional vehicle fields remain null when no extraction rule exists. This is deterministic extraction, not a complete AI valuation engine.

## Verification

```bash
dotnet test CarShow.Tests/CarShow.Tests.csproj
```

Tests use isolated SQLite databases, the real repositories/service/queue/worker/extractor, controlled HTML HTTP fixtures, and ASP.NET TestServer with signed test JWTs. Fixtures exist only in the test project. Eight tests cover 200-channel import, duplicate import, invalid input, persisted queued jobs, pause/resume, message/listing deduplication, normalized Persian values, failed attempts/backoff, channel isolation, restart recovery, JWT-protected routes and JSON DTO binding. They do not verify SQL Server migrations or live Telegram access.

During this change, live `t.me/s/telegram` returned an environment-provided `Site Unavailable` HTML page. No live collection success is claimed. SQL Server and a real Admin login were unavailable, so the full browser → deployed API → SQL Server → Telegram acceptance test remains to be run in your environment. The 200-job fixture run completed in 21.15 seconds with 199 successful jobs and one access failure; managed memory was 8,154,120 bytes before and 7,665,000 bytes after the run (GC snapshots, not peak process memory or a production benchmark). Build reports existing nullable and package-advisory warnings; those are separate from compilation errors.
