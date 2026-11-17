# Telegram ingestion — Phase 1 (raw, unfiltered)

This branch adds an **offline, read-only audit** and a **starter fixture set** from the 2026-11-17 diagnostic export. The production diagnostic endpoint already returns `rawTelegramMessages` (PR #73); this phase does not modify the production parser, DB matcher, or reference-price logic.

## Run

```bash
python tools/telegram-diagnostics/audit_raw_telegram.py response_1794894156562.json --out telegram-audit.json
cd tools/telegram-diagnostics && python -m unittest -v test_phase1.py
```

## Guarantees

- Audit counts **every** `rawTelegramMessages` entry, including empty, non-car, price-less, and unmatched messages.
- No model, trim, year, or price predicate is used before counting.
- Audit is offline; no database or Telegram access and no writes to application data.
- 21 curated **shortened** message examples cover prices, years, mileage, ambiguous numbers, lists and absent fields. Contact details were omitted. These are **not** complete verbatim messages; the full original export remains the source of truth.
- `expected` fields are **preliminary review labels** and are not claimed to be correct parser outputs. The next phase requires manual label verification and C# parser regression tests.
- The exported diagnostic data may contain personal contact information; do not commit the entire raw export to the repository.

## Next step

Verify fixture labels against original raw text, add C# parser-level tests, and then implement extraction improvements in a separate PR. Do not merge parser changes without an accuracy report.
