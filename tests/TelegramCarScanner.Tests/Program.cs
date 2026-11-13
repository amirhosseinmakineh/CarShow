using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarShow.ApplicationService.Services;
using CarShow.Controllers;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Migrations;
using CarShow.Infrastracture.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ParsedCar = CarShow.Controllers.TelegramImportController.ParsedCar;
using Reference = CarShow.Controllers.TelegramImportController.CarPriceReference;

if (args.Contains("--live"))
{
    var liveOptions = new DbContextOptionsBuilder<CarShowContext>().UseSqlite("Data Source=:memory:").Options;
    await using var liveDb = new CarShowContext(liveOptions);
    var liveController = new TelegramImportController(NullLogger<TelegramImportController>.Instance, liveDb);
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
    var result = (ObjectResult)await liveController.GetDiagnostics(deadline.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { StatusCode = result.StatusCode ?? 200, result.Value }));
    return;
}

var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); count++; Console.WriteLine("PASS: " + name); }
var type = typeof(TelegramImportController);
var parse = type.GetNestedType("TelegramCarParser", BindingFlags.NonPublic)!.GetMethod("Parse")!;
var build = type.GetNestedType("CarPriceReferenceBuilder", BindingFlags.NonPublic)!.GetMethod("Build")!;
List<ParsedCar> Parse(string text) => (List<ParsedCar>)parse.Invoke(null, [text])!;
List<Reference> Build(List<ParsedCar> cars) => (List<Reference>)build.Invoke(null, [cars])!;
var cars = Parse("پژو 207 مدل 1403\nقیمت 1500 میلیون\nدنا مدل 1402\nقیمت 1800 میلیون");
Check(cars.Count == 2 && cars[0].Year == "1403" && cars[1].Year == "1402", "Multi-car parser: separate names and years");
Check(cars[0].Price == "1500 میلیون" && cars[1].Price == "1800 میلیون", "Multi-car parser: individual asking prices");
Check(Build(cars).Count == 0, "Singleton groups produce no reference");
var refs = Build([..cars, new() { Name = "پژو 207 اتومات", Year = "1403", Price = "1700 میلیون" }]);
Check(refs.Count == 1 && refs[0].ReferencePriceBillion == 1.600m && refs[0].AdvertisementCount == 2, "Attached grouping and arithmetic average");
Check(Build([new() { Name = "دنا", Year = "1402", Price = "1500 میلیون" }, new() { Name = "دنا", Year = "1403", Price = "1700 میلیون" }]).Count == 0, "Different years remain separate");
Check(Build([new() { Name = "دنا", Year = null!, Price = "1500 میلیون" }, new() { Name = "دنا", Year = null!, Price = "1700 میلیون" }]).Count == 0, "Missing years excluded from references");
var channels = (string[])type.GetField("Channels", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
Check(channels.SequenceEqual(new[] { "autopalermo", "davoodkelasii", "tajcarsefr", "autozamanzade", "miladforoud", "autoland41", "haminkhodroo", "adib_khodro_qom" }), "Exact eight attached channels in original order");
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "CarShow.slnx"))) root = root.Parent;
if (root != null)
{
    var source = File.ReadAllText(Path.Combine(root.FullName, "CarShow/Controllers/TelegramImportController.cs")).Replace("\r\n", "\n");
    var readerAndParser = source[source.IndexOf("        private static HttpClient CreateHttpClient()", StringComparison.Ordinal)..source.IndexOf("        private static class CarPriceReferenceBuilder", StringComparison.Ordinal)];
    var priceConverter = source[source.IndexOf("            public static bool TryConvertPrice(", StringComparison.Ordinal)..source.IndexOf("            private static bool IsValidPrice(", StringComparison.Ordinal)];
    Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(readerAndParser))) == "f2d7e8a7535eb9ef983d602895d8029d27e7f2deb9ad1fdbcc78a0bdb8e2695f", "Attached reader and parser unchanged");
    Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(priceConverter))) == "fe5b09ec5eff09407af798213a9a0afe8911319537b17d10a0910df711a03122", "Attached price converter unchanged");
    Check(!File.Exists(Path.Combine(root.FullName, "CarShow.Infrastracture/Telegram/TelegramCarService.cs")) && !File.Exists(Path.Combine(root.FullName, "CarShow/Services/TelegramCarScanJobManager.cs")) && !File.Exists(Path.Combine(root.FullName, "CarShow/Controllers/TelegramCarController.cs")), "Old Telegram execution paths removed");
}
// Each scenario uses a real file-backed relational database and reopens it to read results.
TelegramImportController Controller(CarShowContext db, HttpClient client) => new(NullLogger<TelegramImportController>.Instance, db, client);
async Task RunScenario(ScanCase scenario, int expectedParsed, int expectedReferences, decimal[] expectedPrices)
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"carshow-reference-{Guid.NewGuid():N}.db");
    var options = new DbContextOptionsBuilder<CarShowContext>().UseSqlite($"Data Source={databasePath};Foreign Keys=True").Options;
    try
    {
        await using (var db = new CarShowContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Cars.Add(new Car { Name = "Existing row", MarketPrice = 123m });
            await db.SaveChangesAsync();
            foreach (var property in new[] { "CarModelId", "CompanyId", "TipId" })
            {
                var entity = db.Model.FindEntityType(typeof(Car))!;
                Check(entity.FindProperty(property)!.IsNullable && !entity.GetForeignKeys().Single(f => f.Properties.Single().Name == property).IsRequired,
                    property + " and relationship optional");
            }
            if (scenario == ScanCase.Normal)
            {
                var services = new ServiceCollection();
                services.AddSingleton(db);
                services.AddSingleton<Microsoft.Extensions.Logging.ILogger<TelegramImportController>>(NullLogger<TelegramImportController>.Instance);
                services.AddHttpClient();
                using var provider = services.BuildServiceProvider();
                var factory = ActivatorUtilities.CreateFactory(typeof(TelegramImportController), Type.EmptyTypes);
                var activated = (TelegramImportController)factory(provider, []);
                var actualClient = (HttpClient)type.GetField("TelegramClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(activated)!;
                Check(actualClient.Timeout == TimeSpan.FromSeconds(30) && actualClient.DefaultRequestHeaders.UserAgent.ToString().Contains("Chrome/120.0.0.0"),
                    "MVC activation retains attached HTTP timeout and User-Agent");
            }
            using var client = new HttpClient(new TelegramStub(scenario));
            var response = await Controller(db, client).Get(CancellationToken.None);
            if (scenario == ScanCase.PartialFailure)
            {
                var partial = (ObjectResult)response.Result!;
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(partial.Value));
                Check(partial.StatusCode == 207 && json.RootElement.GetProperty("Status").GetString() == "PartialSuccess" &&
                    json.RootElement.GetProperty("ChannelErrors").TryGetProperty("davoodkelasii", out _), "Partial scan exposes channel error without discarding healthy references");
            }
            if (expectedReferences == 0)
            {
                Check(response.Result is ObjectResult { StatusCode: 422 }, scenario + ": no references returns 422");
                var payload = JsonSerializer.Serialize(((ObjectResult)response.Result!).Value);
                using var json = JsonDocument.Parse(payload);
                Check(json.RootElement.GetProperty("SavedCars").GetInt32() == 0 && json.RootElement.GetProperty("ParsedCars").GetInt32() == expectedParsed &&
                      json.RootElement.GetProperty("ReferenceCount").GetInt32() == 0, scenario + ": 422 reports zero saved/reference cars");
            }
            else
            {
                Check(response.Result is ObjectResult { StatusCode: 200 or 207 }, scenario + ": references returned successfully");
                var payload = JsonSerializer.Serialize(((ObjectResult)response.Result!).Value);
                using var json = JsonDocument.Parse(payload);
                Check(json.RootElement.GetProperty("SavedCars").GetInt32() == expectedReferences &&
                      json.RootElement.GetProperty("ParsedCars").GetInt32() == expectedParsed &&
                      json.RootElement.GetProperty("ReferenceCount").GetInt32() == expectedReferences &&
                      json.RootElement.GetProperty("References").GetArrayLength() == expectedReferences,
                      scenario + ": API distinguishes parser count, references and saved rows");
            }
        }
        await using (var db = new CarShowContext(options))
        {
            var rows = await db.Cars.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
            Check(rows.Count == 1 + expectedReferences && rows[0].Name == "Existing row" && rows[0].MarketPrice == 123m,
                scenario + ": reopened database preserves existing row and stores only references");
            var finalCars = rows.Skip(1).ToList();
            Check(finalCars.Select(c => c.MarketPrice).Order().SequenceEqual(expectedPrices.Order()),
                scenario + ": stored reference prices in toman");
            Check(finalCars.All(c => c.CompanyId is null && c.CarModelId is null && c.TipId is null),
                scenario + ": saved references have null catalog IDs");
            foreach (var row in finalCars)
            {
                using var details = JsonDocument.Parse(row.Description);
                Check(details.RootElement.GetProperty("Type").GetString() == "TelegramPriceReference" &&
                      details.RootElement.GetProperty("Year").GetString() is not null &&
                      details.RootElement.GetProperty("AdvertisementCount").GetInt32() >= 2 &&
                      details.RootElement.GetProperty("ReferencePriceBillion").GetDecimal() * 1_000_000_000m == row.MarketPrice,
                      scenario + ": persisted year and final comparison details match MarketPrice");
            }
            if (scenario == ScanCase.Normal)
            {
                var service = new CarService(
                    new BaseRepository<long, Car>(db),
                    new BaseRepository<long, Tip>(db),
                    new BaseRepository<long, CarModel>(db),
                    new BaseRepository<long, Category>(db),
                    new BaseRepository<long, Company>(db));
                var listed = await service.GetAllCars(null, null, null, pageSize: 1, pageNumber: 0);
                Check(listed.Count == 1 && listed[0].Id == finalCars[0].Id &&
                      listed[0].MarketPrice == 1_600_000_000m && listed[0].CompanyName == string.Empty,
                    "GET /api/Car service lists the newest saved reference with a null company");
            }
        }
    }
    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(databasePath); }
}
await RunScenario(ScanCase.Singleton, 1, 0, []);
await RunScenario(ScanCase.Normal, 3, 1, [1_600_000_000m]);
await RunScenario(ScanCase.PartialFailure, 3, 1, [1_600_000_000m]);
await RunScenario(ScanCase.DifferentYear, 2, 0, []);
await RunScenario(ScanCase.Invalid, 2, 0, []);
await RunScenario(ScanCase.MultipleGroups, 4, 2, [1_600_000_000m, 1_850_000_000m]);

var failurePath = Path.Combine(Path.GetTempPath(), $"carshow-failures-{Guid.NewGuid():N}.db");
var failureOptions = new DbContextOptionsBuilder<CarShowContext>().UseSqlite($"Data Source={failurePath};Foreign Keys=True").Options;
try
{
    await using (var db = new CarShowContext(failureOptions)) await db.Database.EnsureCreatedAsync();
    foreach (var failure in new[] { ScanCase.HttpFailure, ScanCase.NoPosts, ScanCase.Timeout })
    {
        await using var db = new CarShowContext(failureOptions);
        using var client = new HttpClient(new TelegramStub(failure));
        Check((await Controller(db, client).Get(CancellationToken.None)).Result is ObjectResult { StatusCode: 502 }, failure + ": retrieval failure returns 502");
        await using var verify = new CarShowContext(failureOptions);
        Check(await verify.Cars.AsNoTracking().CountAsync() == 0, failure + ": failed scan does not insert");
    }
    await using (var db = new CarShowContext(failureOptions))
    {
        using var client = new HttpClient(new TelegramStub(ScanCase.Normal));
        var diagnostics = (ObjectResult)await Controller(db, client).GetDiagnostics(CancellationToken.None);
        Check(diagnostics.StatusCode == 200, "GET diagnostics executes successfully");
        using (var partialClient = new HttpClient(new TelegramStub(ScanCase.PartialFailure)))
        {
            var partialDiagnostics = (ObjectResult)await Controller(db, partialClient).GetDiagnostics(CancellationToken.None);
            Check(partialDiagnostics.StatusCode == 207, "GET diagnostics reports partial retrieval explicitly");
        }
        using (var yearClient = new HttpClient(new TelegramStub(ScanCase.Gregorian)))
        {
            var yearDiagnostics = (ObjectResult)await Controller(db, yearClient).GetDiagnostics(CancellationToken.None);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(yearDiagnostics.Value));
            Check(json.RootElement.GetProperty("Examples")[0].GetProperty("Year").GetString() == "1402/1403" &&
                json.RootElement.GetProperty("ValidExamples")[0].GetProperty("Year").GetString() == "1402/1403", "GET diagnostics converts both example year fields at output only");
        }
        var years = new List<ParsedCar>
        {
            new() { Name = "پژو 207", Year = "2024", Price = "1.5 میلیارد" },
            new() { Name = "پژو 207", Year = "2024", Price = "1.7 میلیارد" }
        };
        var parsedDiagnostics = (OkObjectResult)Controller(db, client).DiagnoseParsedCars(years);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(parsedDiagnostics.Value)))
        {
            Check(json.RootElement.GetProperty("References")[0].GetProperty("Year").GetString() == "1402/1403", "POST diagnostics displays Gregorian manufacturing year as Persian range");
            Check(json.RootElement.GetProperty("References")[0].GetProperty("ReferencePriceBillion").GetDecimal() == 1.6m, "Display conversion preserves reference calculation");
        }
        Check(years.All(car => car.Year == "2024"), "Display conversion never mutates internal years");
        Check(Controller(db, client).DiagnoseParsedCars(null!) is BadRequestObjectResult, "POST diagnostics null input rejected");
        Check(await db.Cars.CountAsync() == 0, "Both diagnostics endpoints never insert");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await Controller(db, client).Get(cancelled.Token); throw new Exception("Cancellation not propagated"); }
        catch (OperationCanceledException) { Check(true, "Caller cancellation propagated"); }
    }
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(failurePath); }
var migration = new MakeCarCatalogReferencesOptional();
var alters = migration.UpOperations.OfType<AlterColumnOperation>().ToList();
Check(alters.Count == 3 && alters.All(x => x.Table == "Cars" && x.IsNullable && x.ClrType == typeof(long)), "Migration alters only three Cars reference columns");
Check(migration.UpOperations.All(x => x is AlterColumnOperation or AddForeignKeyOperation or DropForeignKeyOperation), "Migration has no data deletion or other table changes");
var sqlOptions = new DbContextOptionsBuilder<CarShowContext>().UseSqlServer("Server=localhost;Database=not-connected;Integrated Security=true;TrustServerCertificate=true").Options;
await using (var sql = new CarShowContext(sqlOptions))
{
    Check(sql.Database.GetMigrations().Contains("20261108223000_MakeCarCatalogReferencesOptional"), "EF discovers new migration");
    var script = string.Join("\n", sql.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations).Select(c => c.CommandText));
    Check(new[] { "CarModelId", "CompanyId", "TipId" }.All(x => script.Contains($"ALTER COLUMN [{x}] bigint NULL")), "SQL Server generates nullable bigint alterations");
}
Console.WriteLine($"ALL {count} CHECKS PASSED. Insert/read-back used SQLite; SQL Server migration generated, not applied to a live server.");

enum ScanCase { Normal, Singleton, DifferentYear, Invalid, MultipleGroups, Gregorian, HttpFailure, PartialFailure, NoPosts, Timeout }
sealed class TelegramStub(ScanCase scenario) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var channel = request.RequestUri!.AbsolutePath.Split('/').Last();
        if (scenario == ScanCase.Timeout) throw new TaskCanceledException("Simulated connection timeout");
        if (scenario == ScanCase.HttpFailure || (scenario == ScanCase.PartialFailure && channel == "davoodkelasii")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        if (scenario == ScanCase.NoPosts) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Unavailable</html>") });
        var html = channel == "autopalermo" ? scenario switch
        {
            ScanCase.Gregorian => Post(channel, 2, "پژو 207 مدل 2024<br>قیمت 1.5 میلیارد") + Post(channel, 1, "پژو 207 مدل 2024<br>قیمت 1.7 میلیارد"),
            ScanCase.Singleton => Post(channel, 1, "پژو 207 مدل 1403<br>قیمت 1500 میلیون"),
            ScanCase.DifferentYear => Post(channel, 2, "پژو 207 مدل 1403<br>قیمت 1500 میلیون") + Post(channel, 1, "پژو 207 مدل 1402<br>قیمت 1700 میلیون"),
            ScanCase.Invalid => Post(channel, 2, "دنا مدل 1403") + Post(channel, 1, "پژو 207<br>قیمت 1500 میلیون"),
            ScanCase.MultipleGroups => Post(channel, 2, "پژو 207 مدل 1403<br>قیمت 1500 میلیون<br>دنا مدل 1402<br>قیمت 1800 میلیون") + Post(channel, 1, "پژو 207 اتومات مدل 1403<br>قیمت 1700 میلیون<br>دنا مدل 1402<br>قیمت 1900 میلیون"),
            _ => Post(channel, 2, "پژو 207 مدل 1403<br>قیمت 1500 میلیون<br>دنا مدل 1402<br>قیمت 1800 میلیون") + Post(channel, 1, "پژو 207 اتومات مدل 1403<br>قیمت 1700 میلیون")
        } : Post(channel, 1, "");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }
    static string Post(string channel, int id, string text) => $"<div data-post='{channel}/{id}'><div class='tgme_widget_message_text'>{text}</div></div>";
}
