using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarShow.BackgroundServices;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

internal static class ArchiveComparisonChecks
{
    private const string ExpectedSqlSha256 = "1bbc1eaf3a86e029a754ca01a7b9c78af23fd7b1b92a1d031d731b9dcf2444ff";

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ArchiveComparison.sql"));
        check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n")))) ==
              ExpectedSqlSha256, "Archive SQL matches the approved query version");

        var server = Environment.GetEnvironmentVariable("CARSHOW_TEST_SQL_SERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            Console.WriteLine("SKIP: SQL Server integration checks require CARSHOW_TEST_SQL_SERVER.");
            return;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        var configuration = new SqlConnectionStringBuilder(server)
        {
            InitialCatalog = "master",
            Pooling = false,
            ConnectTimeout = 3
        };
        await using var admin = new SqlConnection(configuration.ConnectionString);
        while (true)
        {
            try
            {
                await admin.OpenAsync(ct);
                break;
            }
            catch (SqlException) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }

        var databaseName = "CarShowArchiveTest_" + Guid.NewGuid().ToString("N");
        configuration.InitialCatalog = databaseName;
        var options = new DbContextOptionsBuilder<CarShowContext>()
            .UseSqlServer(configuration.ConnectionString).Options;
        await using var cleanup = new CarShowContext(options);
        try
        {
            await cleanup.Database.EnsureCreatedAsync(ct);
            await using var sqlConnection = new SqlConnection(configuration.ConnectionString);
            await sqlConnection.OpenAsync(ct);
            await VerifyAsync(sqlConnection, options, sql, check, ct);
        }
        finally
        {
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS [{databaseName}]";
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task VerifyAsync(SqlConnection sqlConnection, DbContextOptions<CarShowContext> options,
        string sql, Action<bool, string> check, CancellationToken ct)
    {
        await ExecuteAsync(sqlConnection, """
            CREATE TABLE dbo.TelegramRawArchiveSnapshots (
                Id bigint IDENTITY PRIMARY KEY,
                ChannelUsername nvarchar(128) NOT NULL,
                TelegramMessageId bigint NOT NULL,
                CollectedAt datetimeoffset NOT NULL,
                Metadata nvarchar(max) NULL
            );
            """, ct);
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long id = 0;

        async Task AddAd(string model, string trim, string year, string price, string? text = null,
            long? messageId = null, int seconds = 0, bool isSale = true, int version = 2)
        {
            var currentId = ++id;
            var metadata = JsonSerializer.Serialize(new
            {
                Version = version,
                OriginalText = text ?? "Ad " + currentId,
                Vehicles = new[] { new { Model = model, Trim = trim, Year = year, Price = price, IsSale = isSale } }
            });
            await using var command = sqlConnection.CreateCommand();
            command.CommandText = """
                INSERT dbo.TelegramRawArchiveSnapshots (ChannelUsername, TelegramMessageId, CollectedAt, Metadata)
                VALUES (N'test', @id, @collected, @metadata);
                """;
            command.Parameters.Add("@id", SqlDbType.BigInt).Value = messageId ?? currentId;
            command.Parameters.Add("@collected", SqlDbType.DateTimeOffset).Value = baseTime.AddSeconds(seconds);
            command.Parameters.Add("@metadata", SqlDbType.NVarChar, -1).Value = metadata;
            await command.ExecuteNonQueryAsync(ct);
        }

        await AddAd("دنا", "اتومات", "۱۴۰۵", "۳,۹ میلیارد", "duplicate");
        await AddAd("دنا", "اتومات", "۱۴۰۵", "۳,۹ میلیارد", "duplicate");
        await AddAd("دنا", "اتومات", "405", "4050", messageId: 500);
        await AddAd("دنا", "اتومات", "405", "4000", messageId: 500, seconds: 1);
        await AddAd("دنا", "اتومات", "405", "3950", messageId: 500, seconds: 1);
        await AddAd("دنا", "اتومات", "۰۴", "4100000");
        await AddAd("دنا", "اتومات", "2025", "۴۲۰۰ میلیون");
        await AddAd("دنا", "دنده", "2026", "2500");
        await AddAd("دنا", "دنده", "1405", "2600");
        await AddAd("دنا", "   ", "1405", "2500");
        await AddAd("دنا", "   ", "1405", "2600");
        await AddAd("Ignored buyer", "Trim", "2026", "4000", isSale: false);
        await AddAd("Ignored archive version", "Trim", "2026", "4000", version: 1);
        await AddAd("Single", "Trim", "2026", "2500");
        await AddAd("Conflicting", "Trim", "2026", "2000");
        await AddAd("Conflicting", "Trim", "2026", "4000");
        foreach (var price in new[] { "1000", "2000", "3000", "4000" })
            await AddAd("No consensus", "Trim", "2026", price);
        foreach (var price in new[] { "2800", "3000", "3200" })
            await AddAd("Dispersion", "Trim", "2026", price);
        foreach (var price in new[] { "3000", "3050", "3100", "5500" })
            await AddAd("Outlier", "Trim", "2026", price);

        var oldYear = new Car
        {
            Name = "دنا اتومات", ImageName = "2025.jpg", IsReferenceApproved = true,
            Description = LegacyDescription("دنا", "اتومات", "2025")
        };
        var sameTrimOtherYear = new Car
        {
            Name = "دنا اتومات", ImageName = "2026.jpg", IsReferenceApproved = true,
            Description = LegacyDescription("دنا", "اتومات", "2026")
        };
        var historyOnly = new Car { Name = "Historical", IsReferenceApproved = true };
        var orderOnly = new Car { Name = "Ordered", IsReferenceApproved = true };
        await using (var setup = new CarShowContext(options))
        {
            setup.Cars.AddRange(oldYear, sameTrimOtherYear, historyOnly, orderOnly, new Car { Name = "Obsolete" });
            await setup.SaveChangesAsync(ct);
            setup.CarPriceHistories.AddRange(new CarPriceHistory { CarId = oldYear.Id },
                new CarPriceHistory { CarId = historyOnly.Id });
            setup.Orders.Add(new Order
            {
                CarId = orderOnly.Id, User = new User { Name = "Test" }, Role = new Role { RoleName = "Test" }
            });
            await setup.SaveChangesAsync(ct);
        }

        var before = await ReadCarsAsync(options, ct);
        var manualRows = new List<string>();
        await using (var command = sqlConnection.CreateCommand())
        {
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                manualRows.Add(Key(values.Select(v => v is DBNull ? null : Convert.ToString(v, CultureInfo.InvariantCulture))));
            }
        }

        await using (var context = new CarShowContext(options))
        {
            var diagnosis = await new TelegramScanService(NullLogger<TelegramScanService>.Instance, context)
                .DiagnoseRejectionsAsync(ct);
            var groups = JsonSerializer.SerializeToElement(diagnosis.Value).GetProperty("Groups").EnumerateArray().ToArray();
            var appRows = groups.Select(group => Key(group.EnumerateObject().Select(property =>
                property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString())));
            check(manualRows.Order(StringComparer.Ordinal).SequenceEqual(appRows.Order(StringComparer.Ordinal)),
                "Application rows match direct SQL execution across all 13 columns");
            check(groups.Length == 8 && groups.Count(group => group.GetProperty("Status").GetString() == "Approved") == 4 &&
                  groups.Select(group => group.GetProperty("Status").GetString()).Distinct().Count() == 3,
                "Trimless and ineligible ads are excluded and the remaining SQL statuses are preserved");
            var pair = groups.Single(group => group.GetProperty("Model").GetString() == "Conflicting");
            check(pair.GetProperty("Status").GetString() == "Approved" &&
                  pair.GetProperty("ReferencePriceToman").GetInt64() == 3_000_000_000L,
                "Two eligible prices beyond the old 15% limit are averaged and approved");
            var outlier = groups.Single(group => group.GetProperty("Model").GetString() == "Outlier");
            check(outlier.GetProperty("Status").GetString() == "PriceDispersion" &&
                  outlier.GetProperty("ValidCount").GetInt32() == 4 &&
                  outlier.GetProperty("RemovedCount").GetInt32() == 0,
                "The 12% dispersion check includes prices beyond the old 15% band");
            var deduplicated = groups.Single(group => group.GetProperty("Trim").GetString() == "اتومات" &&
                                                       group.GetProperty("Year").GetInt32() == 2026);
            check(deduplicated.GetProperty("Model").GetString() == "دنا" &&
                  deduplicated.GetProperty("TotalCount").GetInt32() == 2 &&
                  deduplicated.GetProperty("ReferencePriceBillion").GetDecimal() == 3.925m &&
                  deduplicated.GetProperty("ReferencePriceToman").GetInt64() == 3_925_000_000L,
                "The newest tied snapshot wins and duplicate ads across messages count once");
        }
        check((await ReadCarsAsync(options, ct)).SequenceEqual(before), "Diagnostics leaves cars intact");

        await ExecuteAsync(sqlConnection, """
            ALTER TABLE dbo.Cars WITH NOCHECK ADD CONSTRAINT CK_Test_Archive_Reject
                CHECK (Name <> N'دنا دنده');
            """, ct);
        try
        {
            await ScanAsync(options, ct);
            check(false, "Insertion should have failed");
        }
        catch (DbUpdateException)
        {
            check((await ReadCarsAsync(options, ct)).SequenceEqual(before), "Failed insertion rolls back deletion and updates");
        }
        await ExecuteAsync(sqlConnection, "ALTER TABLE dbo.Cars DROP CONSTRAINT CK_Test_Archive_Reject;", ct);

        // Hold the archive table to keep the first scan in progress while the second attempts entry.
        await using (var blocker = sqlConnection.CreateCommand())
        {
            blocker.CommandText = "BEGIN TRANSACTION; SELECT COUNT(*) FROM dbo.TelegramRawArchiveSnapshots WITH (TABLOCKX, HOLDLOCK);";
            await blocker.ExecuteScalarAsync(ct);
        }
        var scan = ScanAsync(options, ct);
        TelegramScanOutcome concurrent;
        try
        {
            concurrent = await ScanAsync(options, ct);
        }
        finally
        {
            await ExecuteAsync(sqlConnection, "ROLLBACK TRANSACTION;", ct);
        }
        check(concurrent.StatusCode == 409, "Overlapping final scans are prevented");
        var result = JsonSerializer.SerializeToElement((await scan).Value);
        var rejections = result.GetProperty("RejectedByReason");
        check(result.GetProperty("activeReferenceCars").GetInt32() == 4 &&
              result.GetProperty("deletedCars").GetInt32() == 2 &&
              result.GetProperty("insertedCars").GetInt32() == 3 &&
              result.GetProperty("retainedHistoricalCars").GetInt32() == 3 &&
              rejections.EnumerateObject().Count() == 2 &&
              rejections.GetProperty("InsufficientAdvertisements").GetInt32() == 1 &&
              rejections.GetProperty("PriceDispersion").GetInt32() == 3,
            "Refresh counts actual writes, retained dependents, active references and SQL rejection statuses");

        var cars = await ReadCarsAsync(options, ct);
        var active = cars.Where(car => car.Approved && !car.Deleted).ToList();
        check(active.Count == 4 && active.Select(car => car.Slug).Distinct().Count() == 4 &&
              active.Count(car => car.Name.StartsWith("دنا", StringComparison.Ordinal)) == 3 &&
              active.Single(car => car.Name == "Conflicting Trim").Price == 3_000_000_000m,
            "One active vehicle per approved SQL row, across manufacturing years and trims");
        check(active.Single(car => car.Id == oldYear.Id).Image == "2025.jpg" &&
              active.Single(car => car.Price == 3_925_000_000m).Image == "2026.jpg",
            "Exact identity retains each model year's image");
        check(cars.Single(car => car.Id == historyOnly.Id).Deleted && cars.Single(car => car.Id == orderOnly.Id).Deleted,
            "Unmatched vehicles with history or orders are retired");
        await using (var context = new CarShowContext(options))
        {
            check(await context.CarPriceHistories.CountAsync(ct) == 2 && await context.Orders.CountAsync(ct) == 1,
                "Dependent history and orders survive replacement");
        }

        await ScanAsync(options, ct);
        var afterRepeat = await ReadCarsAsync(options, ct);
        check(afterRepeat.Where(car => car.Approved && !car.Deleted).Select(car => car.Slug).Order()
                .SequenceEqual(active.Select(car => car.Slug).Order()), "Repeated refreshes do not duplicate active vehicles");
        await ExecuteAsync(sqlConnection, "DELETE dbo.TelegramRawArchiveSnapshots;", ct);
        var empty = JsonSerializer.SerializeToElement((await ScanAsync(options, ct)).Value);
        check(empty.GetProperty("refreshSkipped").GetBoolean() &&
              empty.GetProperty("activeReferenceCars").GetInt32() == 4 &&
              (await ReadCarsAsync(options, ct)).SequenceEqual(afterRepeat), "Zero approved SQL groups preserve cars");

        await ExecuteAsync(sqlConnection, "DROP TABLE dbo.TelegramRawArchiveSnapshots;", ct);
        try
        {
            await ScanAsync(options, ct);
            check(false, "SQL execution should have failed");
        }
        catch (SqlException)
        {
            check((await ReadCarsAsync(options, ct)).SequenceEqual(afterRepeat), "Failed SQL execution preserves cars");
        }
    }

    private static string Key(IEnumerable<string?> fields) => JsonSerializer.Serialize(fields);

    private static string LegacyDescription(string model, string trim, string year)
        => JsonSerializer.Serialize(new { Type = "TelegramPriceReference", ModelName = model, TipName = trim, Year = year });

    private static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<TelegramScanOutcome> ScanAsync(DbContextOptions<CarShowContext> options, CancellationToken ct)
    {
        await using var context = new CarShowContext(options);
        return await new TelegramScanService(NullLogger<TelegramScanService>.Instance, context).ScanAsync(ct);
    }

    private sealed record CarState(long Id, string Name, string Slug, string Image, decimal Price, bool Approved, bool Deleted);

    private static async Task<List<CarState>> ReadCarsAsync(DbContextOptions<CarShowContext> options, CancellationToken ct)
    {
        await using var context = new CarShowContext(options);
        return await context.Cars.OrderBy(car => car.Id).Select(car =>
            new CarState(car.Id, car.Name, car.Slug, car.ImageName, car.MarketPrice, car.IsReferenceApproved, car.IsDelete))
            .ToListAsync(ct);
    }
}
