using System.Text.Json;
using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

// Explicit local SQLite probe. Never reads or connects to the production connection string.
var configPath = Path.GetFullPath(args.FirstOrDefault(x => x.EndsWith(".json")) ?? "CarShow/appsettings.json");
var channels = args.Where(x => x.StartsWith('@')).ToArray();
if (channels.Length == 0) throw new ArgumentException("Pass at least one public @channel.");
var configuration = new ConfigurationBuilder().AddJsonFile(configPath).Build();
var databasePath = Path.Combine(Path.GetTempPath(), $"carshow-zero-probe-{Guid.NewGuid():N}.db");
await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite($"Data Source={databasePath}").Options);
await db.Database.EnsureCreatedAsync();
var services = new ServiceCollection(); services.AddHttpClient("TelegramWebPreview", client => { client.Timeout = TimeSpan.FromSeconds(30); client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 CarShowZeroProbe/1.0"); });
await using var provider = services.BuildServiceProvider();
var collector = new TelegramCollector(configuration, provider.GetRequiredService<IHttpClientFactory>(), NullLogger<TelegramCollector>.Instance);
var service = new ZeroVehicleService(new ZeroVehicleStore(db), collector, configuration);
foreach (var channel in channels)
{
    var run = await service.EnqueueAsync(new ZeroImportRequest(channel, 100), default);
    var claimed = await service.ClaimAsync(DateTime.UtcNow, default);
    if (claimed != run.Id) throw new InvalidOperationException("Unexpected queued run.");
    await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
    Console.WriteLine(JsonSerializer.Serialize(await service.GetRunAsync(run.Id, default)));
}
Console.WriteLine(JsonSerializer.Serialize(new { DatabasePath = databasePath, AcceptedListings = await db.Set<ZeroVehicleListing>().CountAsync(), ReferenceGroups = await db.Set<ZeroVehiclePriceGroup>().CountAsync(), OldCars = await db.Cars.CountAsync(), OldListings = await db.CarListings.CountAsync() }));
