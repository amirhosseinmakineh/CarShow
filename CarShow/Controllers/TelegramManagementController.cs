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
