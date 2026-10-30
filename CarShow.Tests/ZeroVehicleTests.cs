using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;
using CarShow.ApplicationService.Services;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;
namespace CarShow.Tests;

public sealed class ZeroVehicleTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string Settings = JsonSerializer.Serialize(new { ZeroVehicles = new {
        MaxPriceAgeDays = 7, Catalog = new[] { new { Name = "پژو 207", Aliases = new[] { "207", "peugeot 207" }, Trims = new[] {
            new { Name = "دنده‌ای", Aliases = new[] { "دنده ای", "دستی" } },
            new { Name = "اتوماتیک", Aliases = new[] { "اتومات" } },
            new { Name = "اتوماتیک پانوراما", Aliases = new[] { "اتومات پانوراما" } }
        } } }
    } });
    private static IConfiguration Config() => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(Settings))).Build();
    private sealed class Collector : ITelegramCollector
    {
        public List<TelegramCollectedMessage> Messages = [];
        public (string Channel, int Limit, long? Before) Last;
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel, CancellationToken ct) => CollectChannelAsync(channel, 100, null, ct);
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel, int limit, long? before, CancellationToken ct)
        {
            Last = (channel, limit, before);
            return Task.FromResult(new TelegramCollectionResult(true, false, Messages, null, limit, 2, "RequestedLimitReached", 100));
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public CarShowContext Db = null!;
        public Collector Collector = new();
        public ZeroVehicleService Service = null!;
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await f.Connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(f.Connection).Options);
            await f.Db.Database.EnsureCreatedAsync();
            f.Service = new(new ZeroVehicleStore(f.Db), f.Collector, Config());
            return f;
        }
        public async Task<ZeroImportDto> Import(string channel, params string[] texts)
        {
            Collector.Messages = texts.Select((text, i) => new TelegramCollectedMessage((i + 1).ToString(), text, Now.AddHours(-1), null, null)).ToList();
            var run = await Service.EnqueueAsync(new(channel, 50, 200), default);
            var claimed = await Service.ClaimAsync(Now, default); Assert.Equal(run.Id, claimed);
            await Service.ProcessAsync(run.Id, Now, default);
            return (await Service.GetRunAsync(run.Id, default))!;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    [Theory]
    [InlineData("صفر")][InlineData("صفر کیلومتر")][InlineData("صفرکیلومتر")]
    [InlineData("0 کیلومتر")][InlineData("۰ کیلومتر")][InlineData("خشک")]
    [InlineData("صفر خشک")][InlineData("کارنکرده")][InlineData("بدون کارکرد")]
    public void RecognizesExplicitZeroClaims(string condition) => Assert.Null(ZeroVehicleRules.ZeroFailure("پژو ۲۰۷ " + condition));
    [Theory]
    [InlineData("لاستیک صفر")][InlineData("رنگ صفر")][InlineData("خرج صفر")][InlineData("صفر تا صد")]
    [InlineData("لاستیک صفر کیلومتر")][InlineData("لاستیک ها کاملا صفر")][InlineData("رنگ بدنه صفر")][InlineData("صفر نمی باشد")][InlineData("صفر کارکرد: ۲۰ هزار")]
    [InlineData("صفر 100 کیلومتر")][InlineData("خشک کارکرده")][InlineData("صفر نیست")]
    public void RejectsAccessoryClaimsAndContradictions(string text) => Assert.NotNull(ZeroVehicleRules.ZeroFailure(text));

    [Fact]
    public async Task StoresOnlyValidatedZeroAdsWithNullableYearAndKeepsOldTablesEmpty()
    {
        await using var f = await Fixture.Create();
        var run = await f.Import("@zero_test", "پژو ۲۰۷ دنده‌ای صفر قیمت: ۸۰۰ میلیون تومان", "پژو 207 اتومات صفر مدل: 1404 قیمت: 900 میلیون تومان",
            "پژو 207 دنده ای لاستیک صفر قیمت: 800 میلیون تومان", "پژو 207 دنده ای صفر کارکرد: 200 قیمت: 800 میلیون تومان",
            "پژو 207 صفر قیمت: 800 میلیون تومان", "خودروی ناشناس صفر دنده ای قیمت: 800 میلیون تومان", "پژو 207 دنده ای صفر قیمت: توافقی");
        Assert.Equal(7, run.Scanned); Assert.Equal(2, run.Accepted); Assert.Equal(5, run.Rejected); Assert.Equal(2, run.Stored);
        Assert.Equal(("zero_test", 50, (long?)200), f.Collector.Last); Assert.Equal(100, run.NextBefore); Assert.Equal(2, run.PagesFetched);
        var ads = await f.Db.Set<ZeroVehicleListing>().ToListAsync();
        Assert.Equal(2, ads.Count); Assert.Contains(ads, x => x.Year is null); Assert.Contains(ads, x => x.Year == 1404);
        Assert.Equal(2, await f.Db.Set<ZeroVehiclePriceGroup>().CountAsync());
        Assert.Empty(await f.Db.Cars.ToListAsync()); Assert.Empty(await f.Db.CarListings.ToListAsync()); Assert.Empty(await f.Db.TelegramRawMessages.ToListAsync());
        var replay = await f.Import("zero_test", "پژو ۲۰۷ دنده‌ای صفر قیمت: ۸۰۰ میلیون تومان");
        Assert.Equal(1, replay.Duplicates); Assert.Equal(0, replay.Stored); Assert.Equal(2, await f.Db.Set<ZeroVehicleListing>().CountAsync());
    }
    [Fact]
    public async Task UsesModelAndTrimAcrossYearsAndComputesMedianWithOneOrManySamples()
    {
        await using var f = await Fixture.Create();
        await f.Import("channel_one", "پژو 207 دنده ای صفر مدل: 1404 قیمت: 800 میلیون تومان");
        var single = await f.Db.Set<ZeroVehiclePriceGroup>().SingleAsync(); Assert.Equal(800000000m, single.ReferencePrice); Assert.Equal(1, single.SampleCount);
        await f.Import("channel_two", "پژو 207 دنده ای صفر مدل: 1405 قیمت: 1000 میلیون تومان");
        Assert.Equal(900000000m, single.ReferencePrice); Assert.Equal(2, single.SampleCount);
        await f.Import("channel_three", "پژو 207 دنده ای صفر قیمت: 900 میلیون تومان");
        await f.Import("channel_four", "پژو 207 دنده ای صفر قیمت: 9000 میلیون تومان");
        Assert.Equal(900000000m, single.ReferencePrice); Assert.Equal(3, single.SampleCount);
        Assert.Equal(800000000m, single.MinPrice); Assert.Equal(1000000000m, single.MaxPrice);
        Assert.Equal("Outlier", (await f.Db.Set<ZeroVehicleListing>().SingleAsync(x => x.Channel == "channel_four")).PricingStatus);
        await f.Import("channel_five", "پژو 207 اتومات پانوراما صفر قیمت: 1200 میلیون تومان");
        Assert.Equal(2, await f.Db.Set<ZeroVehiclePriceGroup>().CountAsync());
        var duplicate = await f.Import("channel_six", "پژو 207 دنده ای صفر قیمت: 900 میلیون تومان");
        Assert.Equal(1, duplicate.Stored); Assert.Equal(3, single.SampleCount);
        Assert.Equal("DuplicateContent", (await f.Db.Set<ZeroVehicleListing>().SingleAsync(x => x.Channel == "channel_six")).PricingStatus);
        await f.Service.RecalculateAsync(Now.AddDays(8), default);
        Assert.Empty(await f.Db.Set<ZeroVehiclePriceGroup>().ToListAsync());
        Assert.All(await f.Db.Set<ZeroVehicleListing>().ToListAsync(), x => Assert.Equal("Stale", x.PricingStatus));
    }
    [Fact]
    public async Task PaginationSearchDetailsAndMembersReadPersistedResults()
    {
        await using var f = await Fixture.Create();
        await f.Import("paging_channel", "پژو 207 دنده ای صفر قیمت: 800 میلیون تومان", "پژو 207 اتومات صفر قیمت: 900 میلیون تومان", "پژو 207 اتومات پانوراما صفر قیمت: 1000 میلیون تومان");
        f.Db.ChangeTracker.Clear();
        var page1 = await f.Service.ListingsAsync(null, null, null, null, 0, 2, default);
        var page2 = await f.Service.ListingsAsync(null, null, null, null, 1, 2, default);
        Assert.Equal(3, page1.Total); Assert.Equal(2, page1.Items.Count); Assert.Single(page2.Items);
        Assert.Empty(page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)));
        Assert.Single((await f.Service.ListingsAsync("پانوراما", null, null, "Included", 0, 10, default)).Items);
        Assert.NotNull(await f.Service.DetailAsync(page1.Items[0].Id, default));
        var prices = await f.Service.PricesAsync(null, 0, 2, default); Assert.Equal(3, prices.Total);
        var members = await f.Service.MembersAsync(prices.Items[0].Id, 0, 10, default); Assert.Single(members!.Items);
    }
    [Fact]
    public async Task AdminApiAuthorizesAndReturnsRealQueuedJobAndServerPagination()
    {
        await using var f = await Fixture.Create();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ZeroVehiclesController).Assembly);
        builder.Services.AddSingleton(f.Service);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/zero-vehicles/listings")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/zero-vehicles/listings")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/zero-vehicles/imports", new { channel = "bad!", messageLimit = 501 })).StatusCode);
        var response = await client.PostAsJsonAsync("/api/admin/zero-vehicles/imports", new { channel = "api_channel", messageLimit = 20 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = (await response.Content.ReadFromJsonAsync<ZeroImportDto>())!;
        Assert.Equal("Queued", run.Status); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/zero-vehicles/imports", new { channel = "api_channel", messageLimit = 20 })).StatusCode);
        Assert.Equal(run.Id, await f.Service.ClaimAsync(Now, default));
        f.Collector.Messages = [new("1", "پژو 207 دنده ای صفر قیمت: 800 میلیون تومان", Now, null, null)];
        await f.Service.ProcessAsync(run.Id, Now, default);
        var page = await client.GetFromJsonAsync<ZeroPage<ZeroListingDto>>("/api/admin/zero-vehicles/listings?page=0&pageSize=1");
        Assert.Single(page!.Items); Assert.Equal(1, page.Total);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/admin/zero-vehicles/listings/{page.Items[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/admin/zero-vehicles/prices/recalculate", null)).StatusCode);
    }
    [Fact]
    public async Task ZeroImportLeavesExistingPipelineAndRowsUnchanged()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CarShowContext>();
        db.TelegramRawMessages.Add(new CarShow.Domain.Models.Telegram.TelegramRawMessage
        {
            Channel = new() { NormalizedIdentifier = "legacy_channel" }, ExternalMessageId = "1",
            MessageText = "شاهین قیمت: 800 میلیون تومان", MessageDate = Now, CollectedAt = Now,
            SourceUrl = "https://t.me/legacy_channel/1"
        });
        await db.SaveChangesAsync();
        var legacy = scope.ServiceProvider.GetRequiredService<TelegramPricingService>();
        await legacy.CalculateAsync(Now, default);
        var oldCar = await db.Cars.SingleAsync();
        var oldId = oldCar.Id; var oldPrice = oldCar.MarketPrice;
        var collector = new Collector { Messages = [new("1", "پژو 207 دنده ای صفر قیمت: 900 میلیون تومان", Now, null, null)] };
        var zero = new ZeroVehicleService(new ZeroVehicleStore(db), collector, Config());
        var run = await zero.EnqueueAsync(new("independent_channel", 50), default);
        await zero.ClaimAsync(Now, default); await zero.ProcessAsync(run.Id, Now, default);
        await legacy.CalculateAsync(Now.AddMinutes(1), default);
        db.ChangeTracker.Clear();
        Assert.Equal(oldId, (await db.Cars.SingleAsync()).Id);
        Assert.Equal(oldPrice, (await db.Cars.SingleAsync()).MarketPrice);
        Assert.Single(await db.CarListings.ToListAsync()); Assert.Single(await db.TelegramRawMessages.ToListAsync());
        Assert.Single(await db.Set<ZeroVehicleListing>().ToListAsync());
        Assert.Empty(await db.TelegramCollectionJobs.ToListAsync());
    }
    [Fact]
    public async Task InvalidAndAmbiguousAdsNeverCreateListingsOrReferencePrices()
    {
        await using var f = await Fixture.Create();
        var report = await f.Import("invalid_channel", "پژو 207 دنده ای اتومات صفر قیمت: 800 میلیون تومان", "پژو 207 دنده ای صفر قیمت: -2 میلیون تومان", "پژو 207 دنده ای صفر قیمت: 1000", "پژو 207 دنده ای صفر کارکرد: 0.5 هزار قیمت: 800 میلیون تومان");
        Assert.Equal(4, report.Rejected); Assert.Equal(0, report.Stored);
        Assert.Empty(await f.Db.Set<ZeroVehicleListing>().ToListAsync());
        Assert.Empty(await f.Db.Set<ZeroVehiclePriceGroup>().ToListAsync());
    }
    [Fact]
    public async Task ProductionHtmlCollectorPagesThroughValidationIntoIndependentDatabase()
    {
        await using var f = await Fixture.Create();
        var handler = new PreviewHandler();
        var collector = new CarShow.Infrastracture.Telegram.TelegramCollector(Config(), new PreviewFactory(handler), Microsoft.Extensions.Logging.Abstractions.NullLogger<CarShow.Infrastracture.Telegram.TelegramCollector>.Instance);
        var service = new ZeroVehicleService(new ZeroVehicleStore(f.Db), collector, Config());
        var run = await service.EnqueueAsync(new("html_channel", 3, 150), default);
        await service.ClaimAsync(Now, default); await service.ProcessAsync(run.Id, Now, default);
        var result = (await service.GetRunAsync(run.Id, default))!;
        Assert.Equal("Completed", result.Status); Assert.Equal(3, result.Scanned); Assert.Equal(2, result.Accepted); Assert.Equal(1, result.Rejected);
        Assert.Equal(2, result.PagesFetched); Assert.Equal(147, result.NextBefore);
        Assert.Contains("before=150", handler.Requests[0]); Assert.Contains("before=148", handler.Requests[1]);
        Assert.Equal(2, await f.Db.Set<ZeroVehicleListing>().CountAsync());
        Assert.Single(await f.Db.Set<ZeroVehiclePriceGroup>().ToListAsync());
        Assert.Empty(await f.Db.TelegramRawMessages.ToListAsync());
    }
    private sealed class PreviewFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class PreviewHandler : HttpMessageHandler
    {
        public List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var first = request.RequestUri.Query.Contains("before=150");
            var ids = first ? new[] {149, 148} : new[] {147, 146};
            var nodes = string.Join("", ids.Select(id => $"<div data-post='html_channel/{id}'><div class='tgme_widget_message_text'>پژو 207 دنده ای {(id == 148 ? "کارکرد 2000" : "صفر")} قیمت: 800 میلیون تومان</div><time datetime='2026-10-31T09:00:00Z'></time></div>"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<section class='tgme_channel_history'>" + nodes + "</section>") });
        }
    }
    [Fact]
    public async Task DistinctAdsFromOneChannelContributeWithoutSplittingYears()
    {
        await using var f = await Fixture.Create();
        await f.Import("same_channel", "پژو 207 دنده ای صفر مدل: 1404 قیمت: 800 میلیون تومان", "پژو 207 دنده ای صفر مدل: 1405 قیمت: 1000 میلیون تومان");
        var group = await f.Db.Set<ZeroVehiclePriceGroup>().SingleAsync();
        Assert.Equal(2, group.SampleCount); Assert.Equal(1, group.SourceCount); Assert.Equal(900000000m, group.ReferencePrice);
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
