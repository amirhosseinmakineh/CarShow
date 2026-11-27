#nullable enable

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarShow.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CarShow.BackgroundServices;

public sealed partial class TelegramScanService
{
    private const string ApprovedStatus = "Approved";
    private static readonly SemaphoreSlim FinalScanGate = new(1, 1);
    private static readonly Lazy<string> ArchiveComparisonSql = new(() =>
    {
        using var stream = typeof(TelegramScanService).Assembly
            .GetManifestResourceStream("CarShow.ArchiveComparison.sql")
            ?? throw new InvalidOperationException("The archive comparison SQL resource is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private async Task<List<ArchiveComparisonResult>> ExecuteArchiveComparisonAsync(CancellationToken ct)
    {
        var connectionString = _db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("CarShow database connection string is missing.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = ArchiveComparisonSql.Value;
        command.CommandTimeout = 120;

        var results = new List<ArchiveComparisonResult>();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ArchiveComparisonResult
            {
                CompanyName = reader.IsDBNull(0) ? null : reader.GetString(0),
                Model = reader.GetString(1),
                Trim = reader.GetString(2),
                Year = reader.GetInt32(3),
                PersianYear = reader.GetInt32(4),
                TotalCount = reader.GetInt32(5),
                ValidCount = reader.GetInt32(6),
                RemovedCount = reader.GetInt32(7),
                MedianPriceBillion = reader.GetDecimal(8),
                MinPriceBillion = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                MaxPriceBillion = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                ReferencePriceBillion = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                ReferencePriceToman = reader.IsDBNull(12) ? null : reader.GetInt64(12),
                Status = reader.GetString(13)
            });
        }
        return results;
    }

    public async Task<TelegramScanOutcome> ScanAsync(CancellationToken cancellationToken)
    {
        if (!await FinalScanGate.WaitAsync(0, cancellationToken))
            return new TelegramScanOutcome(409, new { Message = "Another Telegram scan is running." });

        try
        {
            return await RunArchiveComparisonAsync(refreshCars: true, cancellationToken);
        }
        finally
        {
            FinalScanGate.Release();
        }
    }

    public Task<TelegramScanOutcome> DiagnoseRejectionsAsync(CancellationToken cancellationToken)
        => RunArchiveComparisonAsync(refreshCars: false, cancellationToken);

    private async Task<TelegramScanOutcome> RunArchiveComparisonAsync(bool refreshCars, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var results = await ExecuteArchiveComparisonAsync(ct);
        var approved = results.Where(x => x.Status == ApprovedStatus).ToList();
        var rejected = results.Where(x => x.Status != ApprovedStatus).ToList();
        var rejectedByReason = rejected.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count());

        if (approved.FirstOrDefault(x => x.ReferencePriceToman is null || x.ReferencePriceBillion is null) is { } invalid)
            throw new InvalidOperationException(
                $"SQL approved {invalid.Model} / {invalid.Trim} / {invalid.Year} without a reference price.");

        var skipped = refreshCars && approved.Count == 0;
        var refresh = refreshCars && !skipped
            ? await SaveReferenceCarsAsync(approved, ct)
            : new ReferenceRefresh(0, 0, [], await CountActiveReferenceCarsAsync(ct));

        var elapsed = watch.ElapsedMilliseconds;
        _logger.LogInformation(
            "Archive comparison ({Operation}): total groups {TotalGroups}, approved {ApprovedGroups}, rejected {RejectedGroups} " +
            "({RejectionsByStatus}), deleted cars {DeletedCars}, inserted cars {InsertedCars}, retained historical cars " +
            "{RetainedHistoricalCars}, active reference cars {ActiveReferenceCars}, execution time {ExecutionTimeMs} ms, " +
            "refresh skipped {RefreshSkipped}.",
            refreshCars ? "refresh" : "diagnostics", results.Count, approved.Count, rejected.Count,
            JsonSerializer.Serialize(rejectedByReason), refresh.Deleted, refresh.Inserted, refresh.Retained.Count,
            refresh.Active, elapsed, skipped);

        if (skipped)
            _logger.LogWarning("Reference refresh skipped because SQL returned zero approved vehicle groups.");

        return new TelegramScanOutcome(200, new
        {
            totalGroups = results.Count,
            approvedGroups = approved.Count,
            rejectedGroups = rejected.Count,
            deletedCars = refresh.Deleted,
            insertedCars = refresh.Inserted,
            retainedHistoricalCars = refresh.Retained.Count,
            activeReferenceCars = refresh.Active,
            executionTimeMs = elapsed,
            refreshSkipped = skipped,
            refreshSkipReason = skipped ? "No approved vehicle groups returned by SQL." : null,
            RejectedByReason = rejectedByReason,
            retainedHistoricalRecords = refresh.Retained,
            Groups = results,
            // Keep the existing admin report fields while exposing every SQL column in Groups.
            ReferenceCount = approved.Count,
            SavedCars = refresh.Inserted,
            References = approved.Select(x => new
            {
                Name = x.Model + " " + x.Trim,
                x.CompanyName,
                ModelName = x.Model,
                TipName = x.Trim,
                Year = x.Year.ToString(CultureInfo.InvariantCulture),
                AdvertisementCount = x.ValidCount,
                x.ReferencePriceBillion
            }).ToList(),
            RejectedReferences = rejected.Select(x => new
            {
                Name = x.Model + " " + x.Trim,
                Year = x.Year.ToString(CultureInfo.InvariantCulture),
                x.Status,
                Reason = x.Status,
                AdvertisementCount = x.TotalCount
            }).ToList()
        })
        {
            ReferenceCount = approved.Count,
            SavedCars = refresh.Inserted
        };
    }

    private sealed record RetainedCar(long CarId, bool HasPriceHistory, bool HasOrders, bool Active);
    private sealed record ReferenceRefresh(int Deleted, int Inserted, List<RetainedCar> Retained, int Active);

    private async Task<ReferenceRefresh> SaveReferenceCarsAsync(
        IReadOnlyList<ArchiveComparisonResult> approved, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cars = new Dictionary<string, Car>(StringComparer.Ordinal);
        var identities = new Dictionary<string, ArchiveComparisonResult>(StringComparer.Ordinal);
        foreach (var row in approved.OrderBy(x => x.Model, StringComparer.Ordinal)
                     .ThenBy(x => x.Trim, StringComparer.Ordinal).ThenBy(x => x.Year)
                     .ThenBy(x => x.TotalCount).ThenBy(x => x.ReferencePriceToman)
                     .ThenBy(x => x.MinPriceBillion).ThenBy(x => x.MaxPriceBillion))
        {
            var car = CreateReferenceCar(row, now);
            var baseSlug = car.Slug;
            for (var suffix = 2; cars.ContainsKey(car.Slug); suffix++)
                car.Slug = baseSlug + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            cars.Add(car.Slug, car);
            identities.Add(car.Slug, row);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = 'CarShow.ArchiveReferenceRefresh',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
                IF @result < 0 THROW 51000, 'Another reference refresh is running.', 1;
                """, ct);

            var companyLookup = (await _db.Companies.OrderBy(x => x.IsDelete).ThenBy(x => x.Id).ToListAsync(ct))
                .GroupBy(x => LookupKey(x.Name)).ToDictionary(x => x.Key, x => x.First());
            var modelLookup = (await _db.CarModels.OrderBy(x => x.IsDelete).ThenBy(x => x.Id).ToListAsync(ct))
                .GroupBy(x => LookupKey(x.Name)).ToDictionary(x => x.Key, x => x.First());
            var tipLookup = (await _db.Tips.OrderBy(x => x.IsDelete).ThenBy(x => x.Id).ToListAsync(ct))
                .GroupBy(x => LookupKey(x.Name)).ToDictionary(x => x.Key, x => x.First());
            foreach (var (slug, car) in cars)
            {
                var row = identities[slug];
                var modelKey = LookupKey(row.Model);
                if (!modelLookup.TryGetValue(modelKey, out var model))
                {
                    model = new CarModel { Name = row.Model };
                    _db.CarModels.Add(model);
                    modelLookup.Add(modelKey, model);
                }
                model.IsDelete = false;
                car.CarModel = model;

                var tipKey = LookupKey(row.Trim);
                if (!tipLookup.TryGetValue(tipKey, out var tip))
                {
                    tip = new Tip { Name = row.Trim };
                    _db.Tips.Add(tip);
                    tipLookup.Add(tipKey, tip);
                }
                tip.IsDelete = false;
                car.Tip = tip;

                if (!string.IsNullOrWhiteSpace(row.CompanyName) && !row.CompanyName.Contains('،'))
                {
                    var key = LookupKey(row.CompanyName);
                    if (!companyLookup.TryGetValue(key, out var company))
                    {
                        company = new Company { Name = row.CompanyName };
                        _db.Companies.Add(company);
                        companyLookup.Add(key, company);
                    }
                    company.IsDelete = false;
                    car.Company = company;
                }
            }
            await _db.SaveChangesAsync(ct);
            foreach (var car in cars.Values)
            {
                car.CarModelId = car.CarModel!.Id;
                car.TipId = car.Tip!.Id;
                car.CompanyId = car.Company?.Id;
            }

            var existing = await _db.Cars
                .Select(car => new
                {
                    Car = car,
                    HasPriceHistory = _db.CarPriceHistories.Any(history => history.CarId == car.Id),
                    HasOrders = _db.Orders.Any(order => order.CarId == car.Id)
                })
                .OrderByDescending(x => x.Car.IsReferenceApproved && !x.Car.IsDelete)
                .ThenByDescending(x => x.Car.LastUpdated)
                .ThenByDescending(x => x.Car.Id)
                .ToListAsync(ct);

            var removable = new List<Car>();
            var retained = new List<RetainedCar>();
            var images = new HashSet<string>(StringComparer.Ordinal);
            var companies = new HashSet<string>(StringComparer.Ordinal);
            var reused = new Dictionary<string, Car>(StringComparer.Ordinal);
            foreach (var old in existing)
            {
                ct.ThrowIfCancellationRequested();
                var protectedByRelationship = old.HasPriceHistory || old.HasOrders;
                var slug = cars.ContainsKey(old.Car.Slug) ? old.Car.Slug : ReadLegacyReferenceSlug(old.Car.Description);
                var active = false;
                if (slug is not null && cars.TryGetValue(slug, out var replacement))
                {
                    if (!string.IsNullOrWhiteSpace(old.Car.ImageName) && images.Add(slug))
                        replacement.ImageName = old.Car.ImageName;
                    if (replacement.CompanyId is null && old.Car.CompanyId is not null && companies.Add(slug))
                        replacement.CompanyId = old.Car.CompanyId;

                    // Keep the ID of an exact match with dependent history or orders.
                    if (protectedByRelationship)
                        active = reused.TryAdd(slug, old.Car);
                }

                if (protectedByRelationship)
                {
                    old.Car.IsReferenceApproved = active;
                    old.Car.IsDelete = !active;
                    retained.Add(new RetainedCar(old.Car.Id, old.HasPriceHistory, old.HasOrders, active));
                }
                else
                {
                    removable.Add(old.Car);
                }
            }

            foreach (var (slug, previous) in reused)
            {
                var replacement = cars[slug];
                previous.Name = replacement.Name;
                previous.Slug = replacement.Slug;
                previous.MarketPrice = replacement.MarketPrice;
                previous.LastUpdated = replacement.LastUpdated;
                previous.Description = replacement.Description;
                previous.ImageName = replacement.ImageName;
                previous.CompanyId = replacement.CompanyId;
                previous.CarModelId = replacement.CarModelId;
                previous.TipId = replacement.TipId;
            }

            var newCars = cars.Where(x => !reused.ContainsKey(x.Key)).Select(x => x.Value).ToList();
            _db.Cars.RemoveRange(removable);
            await _db.SaveChangesAsync(ct);
            _db.Cars.AddRange(newCars);
            await _db.SaveChangesAsync(ct);

            var activeCount = await CountActiveReferenceCarsAsync(ct);
            if (activeCount != approved.Count)
                throw new InvalidOperationException(
                    $"Reference refresh produced {activeCount} active cars for {approved.Count} approved SQL groups.");

            await transaction.CommitAsync(ct);
            return new ReferenceRefresh(removable.Count, newCars.Count, retained, activeCount);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Could not replace reference cars.");
            try
            {
                if (transaction.GetDbTransaction().Connection is not null)
                    await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackError)
            {
                _logger.LogWarning(rollbackError, "The reference refresh transaction could not be rolled back explicitly.");
            }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private Task<int> CountActiveReferenceCarsAsync(CancellationToken ct)
        => _db.Cars.CountAsync(x => x.IsReferenceApproved && !x.IsDelete, ct);

    private static Car CreateReferenceCar(ArchiveComparisonResult row, DateTime now) => new()
    {
        Name = row.Model + " " + row.Trim,
        Slug = ReferenceSlug(row.Model, row.Trim, row.Year.ToString(CultureInfo.InvariantCulture)),
        MarketPrice = row.ReferencePriceToman!.Value,
        IsReferenceApproved = true,
        IsDelete = false,
        LastUpdated = now,
        // The current Cars entity stores per-reference year and price metadata in Description.
        Description = JsonSerializer.Serialize(new
        {
            Type = "TelegramPriceReference",
            row.CompanyName,
            ModelName = row.Model,
            TipName = row.Trim,
            Year = row.Year.ToString(CultureInfo.InvariantCulture),
            YearCalendar = "Gregorian",
            row.PersianYear,
            PriceSampleCount = row.ValidCount,
            AdvertisementCount = row.ValidCount,
            row.ReferencePriceBillion,
            row.ReferencePriceToman,
            ComparisonApproved = true,
            PriceStatus = row.Status,
            PriceCalculatedAt = now,
            ComparedAtUtc = now
        })
    };

    private static string LookupKey(string name) => name.Trim().Replace('ي', 'ی').Replace('ك', 'ک')
        .ToUpperInvariant();

    private static string ReferenceSlug(string model, string trim, string year)
    {
        var identity = JsonSerializer.SerializeToUtf8Bytes(new[] { model, trim, year });
        return "archive-" + Convert.ToHexStringLower(SHA256.HashData(identity));
    }

    private static string? ReadLegacyReferenceSlug(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        try
        {
            using var document = JsonDocument.Parse(description);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("ModelName", out var model) && model.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("TipName", out var trim) && trim.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("Year", out var year) && year.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return ReferenceSlug(model.GetString()!, trim.GetString()!, year.ToString());
        }
        catch (JsonException)
        {
            // Manually entered descriptions may be plain text.
        }
        return null;
    }
}
