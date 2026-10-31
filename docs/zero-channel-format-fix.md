# Dealer channel format regression

The first production `aliajamihavaleh` run completed transport successfully: 100 messages, 7 pages, 0 accepted. The persisted report contained 39 `CONTRADICTORY_CONDITION`, 6 `POSITIVE_MILEAGE`, 29 `ZERO_NOT_CONFIRMED`, 17 `MODEL_UNKNOWN_OR_AMBIGUOUS`, and 9 `TRIM_UNKNOWN_OR_AMBIGUOUS` rejections. Rejected message bodies were not stored.

Public channel captions confirmed several unsupported formats:

- `@aliajamihavaleh👉صفر` / `@aliajami_used_car👉کارکرده` are channel navigation, sometimes flattened onto the caption line. They must not confirm or contradict the vehicle condition.
- A single explicit amount such as `2950م`, `2.045 میلیارد تومان` or `2045000000 تومان` can appear without `قیمت:`. Bare numbers and multiple unlabelled amounts remain rejected.
- Persian Tara trim spellings (`وی یک پلاس`, `وی 4`) and `پانا اتومات` need explicit catalog aliases.
- Actual captions 109136 and 109137 describe a Peugeot 207 hydraulic TU5P at 2,950 million toman. The new distinct hydraulic TU5P trim requires BOTH hydraulic wording and an explicit approved TU5P engine alias. Hydraulic messages without an identified supported engine cannot fall back to the generic manual/automatic trims.

## Behavior

`ZeroVehicleRules` strips social navigation before interpreting vehicle condition and fingerprinting. Mileage and genuine used/contradictory statements remain rejection reasons. Offers for allocation rights, preorders and identifiable buyer requests are rejected explicitly.

`ZeroVehicles:Catalog[].Trims[]` supports optional `RequiredAliasGroups` (every group must have at least one matching alias) and `ExcludedAliases` (any match rejects that trim). These are configuration constraints, applied to the matching canonical model and trim names. No inference is made from a missing engine or trim. No database migration is required; the existing zero catalog seeding updates aliases and adds the configured independent trim on the next import.

The existing collector, used-vehicle processing services, API contracts and database tables are unchanged. Multi-car/multi-price posts remain subject to ambiguity rejection; this patch does not split one Telegram message into several listings. Unlabelled fractional shorthand such as `1.194م` is not reinterpreted as 1,194 million.

## Verification

The original rules are run against the new public-caption persistence regressions to demonstrate failure, followed by the corrected rules. The focused command is:

```sh
dotnet test CarShow.Tests/CarShow.Tests.csproj --filter 'FullyQualifiedName~ZeroVehicleTests|FullyQualifiedName~TelegramMinimalRegistrationTests|FullyQualifiedName~TelegramReferencePricingTests'
```

The zero tests use a real isolated SQLite database. They check real public-caption wording, production catalog configuration, nullable years, exact prices, saved external IDs, median groups, duplicate-content exclusion, rejection of unknown/different engine variants, footers which cannot establish zero status, and replay without duplicate inserts. The existing 27 focused used-vehicle tests are included.

Result: 78 tests passed (51 zero-vehicle cases and 27 existing minimal/reference-pricing cases). With the original rules restored temporarily, both new persistence regressions failed; the observed October captions stored zero records. With the corrected rules, both observed captions store at 2,950,000,000 toman and produce one nonduplicated reference sample. The frontend production build passed; rejection reasons now open automatically when no ads are accepted and the new rejection codes have Persian labels.

The production database was only read to inspect the failed run. These code changes have not been deployed and that production import has not been rerun with the corrected processor. Public Telegram preview transport works from the deployed environment; this workspace's direct preview request returns `Site Unavailable`, so no live import success from this workspace is claimed.
