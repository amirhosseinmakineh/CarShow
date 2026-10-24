# Foreign backend rollout

Runflare project `carshowde1023` (36879), Hetzner Germany; service `carshowapi` (85729), Docker port 5000.
Approved incremental quota: 1 GiB RAM, 0.5 GHz CPU, 2 GiB storage, one service; quoted 239,998 Toman/month, 333.33/hour.

## Configuration

Set secrets in Runflare, never in Git:
- `JwtSettings__SecretKey`: reuse the current valid signing key for session continuity; at least 32 UTF-8 bytes.
- `JwtSettings__Issuer` and `JwtSettings__Audience`: identical to current production.
- `ConnectionStrings__CarShow`: SQL Server connection with `Encrypt=True;TrustServerCertificate=False`, validated server certificate and the existing database name. Prefer a restricted application account. The current remote endpoint is `remote-pishgaman.runflare.com,32578`; connectivity and certificate still need testing from Germany.
- `PORT=5000`, `ASPNETCORE_ENVIRONMENT=Production`.
- `Database__ApplyMigrationsOnStartup=false`.
- `Telegram__WorkersEnabled=false` while validating alongside the old backend.
- `Cors__AllowedOrigins__0=https://mayakhodro.com`, `Cors__AllowedOrigins__1=https://www.mayakhodro.com`; add only the actual staging frontend origin.

Use the feature branch for deployment. Build Docker at repository root. `/health/live` checks the process, `/health/ready` checks SQL connectivity. No database migrations were added by this change. Run migrations only after verifying the existing migration history and a restorable backup.

Run `dotnet CarShow.dll --telegram-probe @foroshekhodro @Baravard` inside the deployed container for a real 500-message-per-channel read-only probe, with per-channel timing, pages, limits and extraction results. It does not require JWT or database access and does not write market prices.

The admin API `POST /api/admin/telegram-channels/preview` accepts `{ "channels": ["@foroshekhodro", "@Baravard"], "messageLimit": 500, "before": null }`. It requires an Admin JWT, returns per-channel reports, and does not write database state. `nextBefore` resumes older messages. Media-only or inaccessible posts are not readable text messages. The public preview can expose fewer than 500; synthetic pagination tests are not evidence of live Telegram availability.

## Existing outage observed before rollout

The old API returned HTTP 503. SQL Server pod `mayadb-qze-deploy-0` was in Error with repeated restarts. Its log reported `PalInstanceId: Unable to read instance id from file` and `PAL initialization failed. Error: 101`. Diagnose disk, permissions and database logs before claiming database availability. Do not delete, initialize, recreate or restore over its files.

## Cutover gates and rollback

1. Verify SQL files, backup and migration history; confirm read and reversible write tests.
2. Verify Login, Admin JWT, order/car APIs and CORS preflight against the new backend.
3. Migrate or share existing uploaded car images and ensure new uploads persist across deployments. Never point the frontend at an empty uploads directory.
4. Frontend feature branch has a `foreign` Angular configuration for isolated validation. Production configuration still uses `https://api.mayakhodro.com/api`.
5. Obtain approval for production cutover. Enable only one effective pricing scheduler and verify no old worker is processing the same jobs. Keep the old service and images.
6. Change the production API routing or build configuration only after all checks. Roll back frontend API routing to `https://api.mayakhodro.com/api` and disable the new workers if regression occurs. No schema change is required to roll back this feature.

No production routing change or merge is authorized by the infrastructure purchase approval alone.
