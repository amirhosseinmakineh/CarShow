using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using CarShow.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarShow.Controllers;

/// <summary>Administrative Telegram scan operations. Does not affect the catalog crawler.</summary>
[ApiController]
[Route("api/admin/telegram")]
[Authorize(Roles = "Admin")]
public sealed class TelegramManagementController : ControllerBase
{
    private readonly TelegramScanBackgroundService _worker;
    private readonly TelegramScanStatus _status;
    private readonly CarShowContext _db;

    public TelegramManagementController(TelegramScanBackgroundService worker, TelegramScanStatus status, CarShowContext db)
    {
        _worker = worker;
        _status = status;
        _db = db;
    }

    [HttpGet("scan-lab/step1")]
    public async Task<IActionResult> GetScanLabStep1(int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return BadRequest("Invalid pagination.");

        var totalCount = await _db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.TelegramRawMessages")
            .SingleAsync(cancellationToken);

        var items = await _db.Database.SqlQueryRaw<ScanLabRow>("""
            SELECT CAST(Id AS bigint) AS Id,
                TRIM(REPLACE(LEFT(MessageText, CHARINDEX(CHAR(10), MessageText + CHAR(10)) - 1), CHAR(13), '')) AS CarName
            FROM dbo.TelegramRawMessages
            """)
            .OrderBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new { items, totalCount, page, pageSize });
    }

    public sealed class ScanLabRow
    {
        public long Id { get; set; }
        public string? CarName { get; set; }
    }

    [HttpPost("scan")]
    public IActionResult StartScan()
    {
        if (!_worker.TryQueueManualScan())
            return Conflict(new { message = "A Telegram scan is already queued or running." });

        return Accepted(new { status = "Queued", message = "Telegram scan queued successfully." });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        await _status.EnsureSchemaAsync(cancellationToken);
        return Ok(await _status.ReadAsync(cancellationToken));
    }

    [HttpGet("results")]
    public async Task<IActionResult> GetResults(CancellationToken cancellationToken)
    {
        await _status.EnsureSchemaAsync(cancellationToken);
        var payload = await _status.ReadResultsAsync(cancellationToken);
        var json = System.Text.Json.JsonSerializer.SerializeToNode(payload);
        var results = json?["results"] as JsonObject;
        var references = results?["References"] as JsonArray ?? results?["references"] as JsonArray;
        if (references is null && json is JsonObject response)
        {
            var rows = await _db.Cars.AsNoTracking()
                .Where(car => car.IsReferenceApproved && !car.IsDelete)
                .Select(car => new { car.Name, car.Description })
                .ToListAsync(cancellationToken);
            references = new JsonArray();
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Description)) continue;
                JsonObject? metadata;
                try { metadata = JsonNode.Parse(row.Description) as JsonObject; }
                catch (System.Text.Json.JsonException) { continue; }
                if (metadata?["Type"]?.ToString() != "TelegramPriceReference") continue;
                if (metadata["CurrentYears"] is not JsonObject years) continue;
                foreach (var year in years)
                {
                    if (year.Value is not JsonObject reference) continue;
                    var item = (JsonObject)reference.DeepClone();
                    item["Name"] = row.Name;
                    item["Year"] = year.Key;
                    item["ModelName"] = metadata["ModelName"]?.DeepClone();
                    item["TipName"] = metadata["TipName"]?.DeepClone();
                    references.Add(item);
                }
            }
            results ??= new JsonObject();
            results["References"] = references;
            results["ReferenceCount"] = references.Count;
            results["ReferenceSource"] = "PersistedApprovedCars";
            if (results.Parent is null) response["results"] = results;
        }
        if (references is not null)
        {
            var ids = references
                .Select(item => item?["Name"]?.GetValue<string>() ?? item?["name"]?.GetValue<string>())
                .Where(name => name is not null && name.StartsWith("catalog:", StringComparison.Ordinal))
                .Select(name => long.TryParse(name!["catalog:".Length..], out var id) ? id : 0)
                .Where(id => id > 0).Distinct().ToArray();

            if (ids.Length > 0)
            {
                var cars = await _db.Cars.AsNoTracking()
                    .Where(car => ids.Contains(car.Id))
                    .Select(car => new {
                        car.Id, car.Name,
                        CompanyName = car.Company != null ? car.Company.Name : "",
                        ModelName = car.CarModel != null ? car.CarModel.Name : "",
                        TipName = car.Tip != null ? car.Tip.Name : ""
                    }).ToDictionaryAsync(car => car.Id, cancellationToken);

                foreach (var item in references.OfType<JsonObject>())
                {
                    var rawName = item["Name"]?.GetValue<string>() ?? item["name"]?.GetValue<string>();
                    if (rawName is null || !rawName.StartsWith("catalog:", StringComparison.Ordinal)
                        || !long.TryParse(rawName["catalog:".Length..], out var id)
                        || !cars.TryGetValue(id, out var car)) continue;

                    item["CarName"] = car.Name;
                    item["CompanyName"] = car.CompanyName;
                    item["ModelName"] = car.ModelName;
                    item["TipName"] = car.TipName;
                }
            }
        }
        return Ok(json);
    }
}
