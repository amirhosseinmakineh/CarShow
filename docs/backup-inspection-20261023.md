# SQL Server backup and disk inspection — 2026-10-23

The user explicitly approved creating a temporary download link and reading the latest mayadb backup. No restore, database-file modification, disk resize or production routing change was performed.

## Verified backup contents

- Latest listed backup: 1405-07-22 05:33 (2026-10-14, Tehran time).
- Archive size: 147 bytes; gzip/tar structure opened successfully.
- SHA-256: `6ff6193201ca83ce26fc3b14f1c3b239add9425f92a40052d7b1a20909e2328b`.
- Exactly one archive member: a directory, with zero data files. No SQL Server data, log or database backup file was present.
- This archive cannot restore the database. This does not establish that the current database files are missing or lost.
- The temporary download URL is deliberately omitted from this repository.

## Current disk

The same persistent disk is still mounted to mayadb at `/var/opt/mssql/`. Runflare displays 3 GB used out of 3 GB capacity and a full red usage bar. A separate 512 MB disk is mounted to the old backend at `/var/opt/mssql/bak`, with about 8 MB used; its contents were not inspected.

The SQL Server runtime previously reported `PalInstanceId: Unable to read instance id from file` and `PAL initialization failed. Error: 101`. The full disk is an observed issue, but its causal relationship to the startup failure has not yet been verified.

## Prepared next action, NOT applied

The disk edit form permits increasing the database disk from 3 GB to 3.5 GB within current project capacity. No project plan upgrade was selected. Runflare warns that disk size cannot subsequently be decreased.

User approval is required under the original production-change gate before saving this resize. After approval: save only the size change, verify SQL startup and existing files, obtain a valid backup, then complete secure backend database configuration, existing-user authentication checks, uploads preservation and production cutover gates.
