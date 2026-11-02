using System.Net;
using System.Text;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.BackgroundServices;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.ZeroVehicles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarShow.Tests;

public sealed class ZeroVehicleVerificationTests
{
    private sealed class RecordedHttp : HttpMessageHandler, IHttpClientFactory
    {
        public int PriceRequests;
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var robots = request.RequestUri!.AbsolutePath == "/robots.txt";
            if (!robots) PriceRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(robots ? "User-agent: *\nDisallow:" : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/carir-prices-20261102.html"))) });
        }
    }
    private sealed class Captions(bool throws = false) : ITelegramCollector
    {
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel, CancellationToken ct)
        {
            if (channel.Contains("broken"))
            {
                if (throws) throw new HttpRequestException("Controlled channel failure");
                return Task.FromResult(new TelegramCollectionResult(false, false, [], "Controlled channel failure"));
            }
            var price = channel.Contains("outlier") ? "۵۰۰۰" : channel.Contains("lower") ? "۱۳۹۰۰" : "۱۴۱۰۰";
            return Task.FromResult(new TelegramCollectionResult(true, false,
                [new("1", $"آیتو M5 مدل 2025 صفر {price}م", DateTime.UtcNow, null, null)], null));
        }
    }
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
        """{"ZeroVehicles":{"Catalog":[{"Name":"آیتو M5 مدل 2025","Aliases":[],"Trims":[]}]}}"""))).Build();

    [Fact]
    public async Task RecordedCarIrExtractionValidationComparisonPersistenceAndReplay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var http = new RecordedHttp(); var config = Configuration();
        var service = new ZeroVehicleService(new ZeroVehicleStore(db), new Captions(), config, new CarIrMarketPriceCrawler(http, config));
        async Task Import(string channel)
        {
            var run = await service.EnqueueAsync(new(channel), default);
            Assert.Equal(run.Id, await service.ClaimAsync(DateTime.UtcNow, default));
            await service.ProcessAsync(run.Id, DateTime.UtcNow, default);
        }
        foreach (var channel in new[] { "outlier_channel", "lower_channel", "upper_channel" }) await Import(channel);
        var group = await db.Set<ZeroVehiclePriceGroup>().SingleAsync();
        Assert.Equal(14_000_000_000m, group.MarketPrice);
        Assert.Equal(14_000_000_000m, group.ReferencePrice);
        Assert.Equal(2, group.SampleCount);
        Assert.Single(await db.Set<ZeroVehicleListing>().Where(x => x.PricingStatus == "Outlier").ToListAsync());
        Assert.Equal(2, await db.Set<ZeroVehicleListing>().CountAsync(x => x.PricingStatus == "Included"));
        await Import("lower_channel");
        Assert.Equal(3, await db.Set<ZeroVehicleListing>().CountAsync());
        Assert.Equal(1, http.PriceRequests);
        Assert.Empty(await db.Cars.ToListAsync()); Assert.Empty(await db.CarListings.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerContinuesOtherChannelsAfterReportedOrThrownFailure(bool throws)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration());
        services.AddDbContext<CarShowContext>(o => o.UseSqlite(connection));
        services.AddScoped<CarShow.Domain.IRepository.IZeroVehicleStore, ZeroVehicleStore>();
        services.AddScoped<ZeroVehicleService>();
        services.AddSingleton<ITelegramCollector>(new Captions(throws));
        services.AddSingleton<IHttpClientFactory>(new RecordedHttp());
        services.AddSingleton<ICarIrMarketPriceCrawler, CarIrMarketPriceCrawler>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            await db.Database.EnsureCreatedAsync();
            // Worker gate only: this SQLite setup does NOT test SQL Server migrations.
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT NOT NULL)");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('20261102050507_ZeroVehicleCarIrPipeline', '10.0.7')");
            var service = scope.ServiceProvider.GetRequiredService<ZeroVehicleService>();
            foreach (var channel in new[] { "broken_channel", "lower_channel", "upper_channel" }) await service.EnqueueAsync(new(channel), default);
        }
        using var worker = new ZeroVehicleWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ZeroVehicleWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                await Task.Delay(250, timeout.Token);
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
                var runs = await db.Set<ZeroVehicleImportRun>().AsNoTracking().ToListAsync(timeout.Token);
                if (runs.All(x => x.Status is "Completed" or "Failed"))
                {
                    Assert.Single(runs, x => x.Status == "Failed");
                    Assert.Equal(2, runs.Count(x => x.Status == "Completed"));
                    Assert.Equal(2, await db.Set<ZeroVehicleListing>().CountAsync(timeout.Token));
                    break;
                }
            }
        }
        finally { await worker.StopAsync(default); }
    }
}
