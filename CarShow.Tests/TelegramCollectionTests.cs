using System.Net;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.Telegram;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramCollectionTests
{
    [Fact]
    public async Task Import200ChannelsPersistsJobsAndCountsDuplicateReimport()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        var input = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"@channel_{i:000}"));
        var result = await service.ImportAsync(new(input), default);
        Assert.Equal(200, result.Created);
        Assert.Equal(200, result.Queued);
        var repeated = await service.ImportAsync(new(input + "\ninvalid!"), default);
        Assert.Equal(0, repeated.Created);
        Assert.Equal(200, repeated.Duplicates);
        Assert.Equal(1, repeated.Invalid);
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        Assert.Equal(200, await db.TelegramChannels.CountAsync());
        Assert.Equal(200, await db.TelegramCollectionJobs.CountAsync());
        Assert.All(await db.TelegramCollectionJobs.ToListAsync(), job =>
            Assert.Equal(TelegramCollectionJobStatus.Queued, job.Status));
    }

    [Fact]
    public async Task PauseResumePersistsAndNormalizesPublicUrls()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
        var result = await service.ImportAsync(new("@Example https://t.me/example/ invalid!"), default);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Duplicates);
        Assert.Equal(1, result.Invalid);
        var channel = Assert.Single(await service.GetChannelsAsync(default));
        Assert.True(await service.SetEnabledAsync(channel.Id, false, default));
        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<CarShowContext>();
        var paused = await db.TelegramChannels.SingleAsync();
        Assert.False(paused.Enabled);
        Assert.Equal(TelegramChannelStatus.Paused, paused.Status);
        Assert.True(await service.SetEnabledAsync(channel.Id, true, default));
        await db.Entry(paused).ReloadAsync();
        Assert.True(paused.Enabled);
        Assert.Equal(TelegramChannelStatus.Pending, paused.Status);
    }

    [Fact]
    public async Task WorkerUsesCollectorPersistsMessagesAndDoesNotDuplicateOnRetry()
    {
        const string preview = """
            <div class="tgme_channel_history">
              <div class="tgme_widget_message_wrap">
                <div data-post="example/42">
                  <div class="tgme_widget_message_text">خودرو: پژو ۲۰۷<br>قیمت: ۸۵۰ میلیون تومان<br>کارکرد: ۲۰ هزار<br>مدل: ۱۴۰۲</div>
                  <time datetime="2026-10-18T00:00:00Z"></time>
                </div>
              </div>
            </div>
            """;
        await using var fixture = await Fixture.CreateAsync(preview);
        long channelId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
            await service.ImportAsync(new("@example"), default);
            channelId = (await service.GetChannelsAsync(default))[0].Id;
        }

        var worker = fixture.Services.GetRequiredService<TelegramCollectionWorker>();
        await worker.StartAsync(default);
        try
        {
            await fixture.WaitForAsync(async db => await db.TelegramCollectionJobs.AnyAsync(x => x.Status == TelegramCollectionJobStatus.Succeeded));
            await fixture.Services.GetRequiredService<ITelegramCollectionQueue>().EnqueueAsync(channelId);
            await fixture.WaitForAsync(async db => await db.TelegramCollectionJobs.CountAsync(x => x.Status == TelegramCollectionJobStatus.Succeeded) == 2);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            Assert.Equal(1, await db.TelegramRawMessages.CountAsync());
            var listing = await db.CarListings.SingleAsync();
            Assert.Equal(850_000_000m, listing.Price);
            Assert.Equal("IRT", listing.Currency);
            Assert.Equal(20_000, listing.Mileage);
            Assert.Equal(1402, listing.Year);
            Assert.Equal("پژو 207", listing.Model);
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task FailureRetriesAndDoesNotPreventAnotherChannelFromRunning()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
            await service.ImportAsync(new("@example @another"), default);
        }

        var worker = fixture.Services.GetRequiredService<TelegramCollectionWorker>();
        await worker.StartAsync(default);
        try
        {
            await fixture.WaitForAsync(async db =>
                await db.TelegramCollectionJobs.CountAsync(x => x.Status == TelegramCollectionJobStatus.Failed) == 4);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            Assert.All(await db.TelegramChannels.ToListAsync(), channel =>
            {
                Assert.Equal(TelegramChannelStatus.Error, channel.Status);
                Assert.Equal(2, channel.RetryCount);
            });
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task InaccessiblePreviewDoesNotReportSuccess()
    {
        await using var fixture = await Fixture.CreateAsync("<h1>Site Unavailable</h1>");
        var collector = fixture.Services.GetRequiredService<ITelegramCollector>();
        var result = await collector.CollectChannelAsync("example", default);
        Assert.False(result.Success);
        Assert.True(result.AccessRequired);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task RestartRecoversPersistedJobsWithAnEmptyMemoryQueue()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            var channel = new TelegramChannel
            {
                TelegramIdentifier = "example",
                NormalizedIdentifier = "example",
                Username = "example",
                PublicUrl = "https://t.me/example",
                Status = TelegramChannelStatus.Processing
            };
            db.TelegramChannels.Add(channel);
            await db.SaveChangesAsync();
            db.TelegramCollectionJobs.Add(new TelegramCollectionJob
            {
                ChannelId = channel.Id,
                Status = TelegramCollectionJobStatus.Processing,
                Attempt = 1
            });
            await db.SaveChangesAsync();
        }

        var worker = fixture.Services.GetRequiredService<TelegramCollectionWorker>();
        await worker.StartAsync(default);
        try
        {
            await fixture.WaitForAsync(async db =>
                await db.TelegramCollectionJobs.CountAsync(x => x.Status == TelegramCollectionJobStatus.Failed) == 3);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            Assert.Contains(await db.TelegramCollectionJobs.ToListAsync(),
                job => job.Error == "Collection interrupted by application restart.");
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    [Fact]
    public async Task WorkerDrains200JobsIncludingAnInaccessibleChannel()
    {
        const string preview = """
            <div class="tgme_channel_history">
              <div class="tgme_widget_message_wrap"><div data-post="example/42">
                <div class="tgme_widget_message_text">قیمت: 850 میلیون تومان</div>
                <time datetime="2026-10-18T00:00:00Z"></time>
              </div></div>
            </div>
            """;
        await using var fixture = await Fixture.CreateAsync(preview, "channel_001");
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<ITelegramChannelService>();
            var input = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"@channel_{i:000}"));
            await service.ImportAsync(new(input), default);
        }

        var memoryBefore = GC.GetTotalMemory(true);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var worker = fixture.Services.GetRequiredService<TelegramCollectionWorker>();
        await worker.StartAsync(default);
        try
        {
            await fixture.WaitForAsync(async db =>
                await db.TelegramCollectionJobs.CountAsync(x => x.Status == TelegramCollectionJobStatus.Succeeded) == 199);
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
            Assert.Equal(1, await db.TelegramCollectionJobs.CountAsync(x => x.Status == TelegramCollectionJobStatus.AccessRequired));
            Assert.Equal(199, await db.TelegramRawMessages.CountAsync());
            Assert.Equal(199, await db.CarListings.CountAsync());
            Assert.Equal(200, await db.TelegramCollectionJobs.CountAsync());
            Console.WriteLine($"200 jobs drained in {stopwatch.Elapsed.TotalSeconds:F2}s; managed memory before={memoryBefore}, after={GC.GetTotalMemory(true)} bytes.");
        }
        finally
        {
            await worker.StopAsync(default);
        }
    }

    internal sealed class Fixture(SqliteConnection connection, ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public static async Task<Fixture> CreateAsync(string? preview = null, string? inaccessibleUsername = null)
        {
            var connection = new SqliteConnection($"Data Source={Path.Combine(Path.GetTempPath(), $"telegram-tests-{Guid.NewGuid():N}.db")}");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:Provider"] = "web-preview",
                ["Telegram:MaxRetries"] = "1",
                ["Telegram:RequestDelayMilliseconds"] = "100"
            }).Build());
            services.AddDbContext<CarShowContext>(options => options.UseSqlite(connection.ConnectionString));
            services.AddScoped(typeof(IBaseRepository<,>), typeof(BaseRepository<,>));
            services.AddScoped<ITelegramChannelService, TelegramChannelService>();
            services.AddSingleton<ITelegramCollectionQueue, TelegramCollectionQueue>();
            services.AddSingleton<ITelegramCollector, TelegramCollector>();
            services.AddScoped<ITelegramListingExtractor, TelegramListingExtractor>();
            services.AddSingleton<TelegramCollectionWorker>();
            services.AddSingleton<IHttpClientFactory>(new FixtureHttpClientFactory(preview, inaccessibleUsername));
            var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CarShowContext>().Database.EnsureCreatedAsync();
            return new Fixture(connection, provider);
        }

        public async Task WaitForAsync(Func<CarShowContext, Task<bool>> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (!timeout.IsCancellationRequested)
            {
                await using var scope = Services.CreateAsyncScope();
                if (await predicate(scope.ServiceProvider.GetRequiredService<CarShowContext>()))
                {
                    return;
                }

                await Task.Delay(50, timeout.Token);
            }

            throw new TimeoutException("Worker did not reach the expected persisted state.");
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await connection.DisposeAsync();
            SqliteConnection.ClearAllPools();
            File.Delete(connection.DataSource);
        }
    }

    // Test-only HTTP fixtures exercise the production HTML collector; no fake provider is registered in the application.
    private sealed class FixtureHttpClientFactory(string? preview, string? inaccessibleUsername) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new FixtureHandler(preview, inaccessibleUsername));
        }
    }

    private sealed class FixtureHandler(string? preview, string? inaccessibleUsername) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = request.RequestUri?.AbsolutePath == $"/s/{inaccessibleUsername}"
                ? HttpStatusCode.Forbidden
                : preview is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(preview ?? "Temporarily unavailable")
            });
        }
    }
}
