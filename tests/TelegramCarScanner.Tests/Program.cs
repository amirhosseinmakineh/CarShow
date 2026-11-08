using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarShow.Controllers;
using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Migrations;
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
    var algorithm = source[source.IndexOf("        private static HttpClient CreateHttpClient()", StringComparison.Ordinal)..].Trim().Replace("public static bool TryConvertPrice(", "private static bool TryConvertPrice(");
    Check(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(algorithm))) == "bcb9a1d7da1b60273b182bfd64b845ed85c1ac619c5c7ed4ae6e1988408eacbd", "Reader/parser/normalization/converter/builder match attached SHA-256");
    Check(!File.Exists(Path.Combine(root.FullName, "CarShow.Infrastracture/Telegram/TelegramCarService.cs")) && !File.Exists(Path.Combine(root.FullName, "CarShow/Services/TelegramCarScanJobManager.cs")) && !File.Exists(Path.Combine(root.FullName, "CarShow/Controllers/TelegramCarController.cs")), "Old Telegram execution paths removed");
}
// Real file-backed relational database, never EF InMemory.
var databasePath = Path.Combine(Path.GetTempPath(), $"carshow-insert-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<CarShowContext>().UseSqlite($"Data Source={databasePath};Foreign Keys=True").Options;
TelegramImportController Controller(CarShowContext db, HttpClient client) => new(NullLogger<TelegramImportController>.Instance, db, client);
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
            Check(entity.FindProperty(property)!.IsNullable && !entity.GetForeignKeys().Single(f => f.Properties.Single().Name == property).IsRequired, property + " and relationship optional");
        }
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<TelegramImportController>>(NullLogger<TelegramImportController>.Instance);
        services.AddHttpClient();
        using (var provider = services.BuildServiceProvider())
        {
            var factory = ActivatorUtilities.CreateFactory(typeof(TelegramImportController), Type.EmptyTypes);
            var activated = (TelegramImportController)factory(provider, []);
            var actualClient = (HttpClient)type.GetField("TelegramClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(activated)!;
            Check(actualClient.Timeout == TimeSpan.FromSeconds(30) && actualClient.DefaultRequestHeaders.UserAgent.ToString().Contains("Chrome/120.0.0.0"), "MVC activation retains attached HTTP timeout and User-Agent");
        }
        using var client = new HttpClient(new TelegramStub(ScanCase.Normal));
        Check((await Controller(db, client).Get(CancellationToken.None)).Result is OkObjectResult, "Successful attached scan returns references");
    }
    await using (var db = new CarShowContext(options))
    {
        var rows = await db.Cars.AsNoTracking().OrderBy(c => c.Id).ToListAsync();
        Check(rows.Count == 4 && rows[0].Name == "Existing row" && rows[0].MarketPrice == 123m, "Reopen DB: existing row preserved, three parser ads inserted");
        var imported = rows.Skip(1).ToList();
        Check(imported.All(c => c.CarModelId is null), "Read back null CarModelId");
        Check(imported.All(c => c.CompanyId is null), "Read back null CompanyId");
        Check(imported.All(c => c.TipId is null), "Read back null TipId");
        Check(imported.Select(c => c.MarketPrice).Order().SequenceEqual(new[] { 1_500_000_000m, 1_700_000_000m, 1_800_000_000m }), "Individual prices persisted, not average");
        Check(imported.Select(c => JsonSerializer.Deserialize<ParsedCar>(c.Description)!.Year).Order().SequenceEqual(new[] { "1402", "1403", "1403" }), "Exact years read back from Description JSON");
    }
    await using (var db = new CarShowContext(options))
    {
        using var client = new HttpClient(new TelegramStub(ScanCase.Unpriced));
        Check((await Controller(db, client).Get(CancellationToken.None)).Result is ObjectResult { StatusCode: 422 }, "No reference returns attached 422 response");
    }
    await using (var db = new CarShowContext(options))
    {
        var row = await db.Cars.AsNoTracking().OrderByDescending(c => c.Id).FirstAsync();
        var raw = JsonSerializer.Deserialize<ParsedCar>(row.Description)!;
        Check(await db.Cars.CountAsync() == 5 && raw.Year is null && raw.Price is null && row.MarketPrice == 0m, "Missing price/year parser ad saved and read back");
    }
    foreach (var failure in new[] { ScanCase.HttpFailure, ScanCase.PartialFailure, ScanCase.NoPosts, ScanCase.Timeout })
    {
        await using var db = new CarShowContext(options);
        using var client = new HttpClient(new TelegramStub(failure));
        Check((await Controller(db, client).Get(CancellationToken.None)).Result is ObjectResult { StatusCode: 502 }, failure + " returns retrieval failure");
        await using var verify = new CarShowContext(options);
        Check(await verify.Cars.CountAsync() == 5, failure + " inserts nothing");
    }
    await using (var db = new CarShowContext(options))
    {
        using var client = new HttpClient(new TelegramStub(ScanCase.Normal));
        await Controller(db, client).GetDiagnostics(CancellationToken.None);
        Check(await db.Cars.CountAsync() == 5, "Diagnostics never insert");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await Controller(db, client).Get(cancelled.Token); throw new Exception("Cancellation not propagated"); }
        catch (OperationCanceledException) { Check(true, "Caller cancellation propagated"); }
    }
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(databasePath); }
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

enum ScanCase { Normal, Unpriced, HttpFailure, PartialFailure, NoPosts, Timeout }
sealed class TelegramStub(ScanCase scenario) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var channel = request.RequestUri!.AbsolutePath.Split('/').Last();
        if (scenario == ScanCase.Timeout) throw new TaskCanceledException("Simulated connection timeout");
        if (scenario == ScanCase.HttpFailure || (scenario == ScanCase.PartialFailure && channel == "davoodkelasii")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        if (scenario == ScanCase.NoPosts) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Unavailable</html>") });
        var html = channel == "autopalermo"
            ? scenario == ScanCase.Unpriced ? Post(channel, 2, "دنا") : Post(channel, 2, "پژو 207 مدل 1403<br>قیمت 1500 میلیون<br>دنا مدل 1402<br>قیمت 1800 میلیون") + Post(channel, 1, "پژو 207 اتومات مدل 1403<br>قیمت 1700 میلیون")
            : Post(channel, 1, "");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }
    static string Post(string channel, int id, string text) => $"<div data-post='{channel}/{id}'><div class='tgme_widget_message_text'>{text}</div></div>";
}
