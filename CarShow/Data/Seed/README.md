# Car catalog import

The catalog import uses the existing `CarModels`, `Companies`, `Tips`, and `Cars` tables.

## Run

1. Start the Web API with an up-to-date SQL Server schema.
2. Authenticate with a JWT carrying the `Admin` role.
3. Send the original HTML catalog as multipart form field `file`:

```bash
curl -X POST "http://localhost:8080/api/admin/catalog/import" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@cars-catalog.html;type=text/html"
```

Only rows marked `verified` are imported. Candidate rows are rejected because their company-to-trim relationship is not confirmed. The import is idempotent for existing (company, model, tip) combinations and runs inside a transaction.

The source HTML is supplied at request time; it is not served from `wwwroot`. This avoids publishing unreviewed source material and allows replacement catalogs without redeployment.

**Before production use:** reconcile the existing EF Core migrations with the current entity model and run the import against a staging database first.
