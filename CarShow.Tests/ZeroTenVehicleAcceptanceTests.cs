using System.Text.Json;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using Microsoft.Extensions.Configuration;
using Xunit;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using CarShow.Infrastracture.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit.Abstractions;
namespace CarShow.Tests;
public sealed class ZeroTenVehicleAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public void ExactOriginalMessageHasTenPartsEightZeroAndCorrectAmounts()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"ZeroVehicleTestSettings.json")).Build();
        var options = ZeroVehicleOptions.Read(configuration);
        long id=0;
        var catalog=options.Catalog.Select(m=>new ZeroVehicleModel { Id=++id,Name=m.Name,Aliases=string.Join('\n',m.Aliases),Trims=m.Trims.Append(new(){Name="نام‌محور"}).Select(t=>new ZeroVehicleTrim {Id=++id,Name=t.Name,Aliases=string.Join('\n',t.Aliases)}).ToList() }).ToList();
        var parts=ZeroVehicleRules.Split(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/ten-vehicle-original.txt")));
        var now=DateTime.UtcNow;
        var rows=parts.Select(p=>new { Part=p.Index+1,Raw=p.Text,Condition=ZeroVehicleRules.ZeroFailure(p.Text),Result=ZeroVehicleRules.Validate(new("901",p.Text,now,null,null),"fixture_channel",catalog,options,now),Amount=ZeroVehicleRules.ExtractPrice(p.Text,options)}).ToArray();
        output.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
        Assert.Equal(10,parts.Count);
        Assert.Equal(new[]{1,3,4,5,6,7,8,9},rows.Where(x=>x.Condition is null).Select(x=>x.Part));
        Assert.All(rows.Where(x=>x.Part is 2 or 10),x=>Assert.Equal("POSITIVE_MILEAGE",x.Condition));
        Assert.All(rows.Where(x=>x.Condition is null),x=>Assert.NotNull(x.Result.Listing));
        Assert.Equal(new[]{"پژو 207 دنده‌ای هیدرولیک TU5P","پژو 207 TU3","پژو 207 TU3","پراید 151","پادرا پلاس","اطلس","تارا","لاماری اکو"},rows.Where(x=>x.Condition is null).Select(x=>x.Result.Listing!.OriginalName));
        Assert.Equal(new[]{2880000000m,2845000000m,2880000000m,1700000000m,2600000000m,2045000000m,4490000000m,6200000000m},rows.Where(x=>x.Condition is null).Select(x=>Assert.Single(x.Amount.Candidates)));
        Assert.All(rows.Where(x=>x.Condition is null),x=>Assert.Equal("901",x.Result.Listing!.ExternalMessageId));
    }

    sealed class Page : HttpMessageHandler,IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var text=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/ten-vehicle-original.txt"));
            // Valid Telegram HTML envelope around the exact user-supplied real caption.
            // This envelope is constructed, NOT a captured live Telegram response.
            var body=request.RequestUri!.Query.Length>0?"":$"<div data-post='fixture_channel/901'><div class='tgme_widget_message_text'>{WebUtility.HtmlEncode(text).Replace("\n","<br>")}</div><time datetime='{DateTime.UtcNow:O}'></time></div>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<div class='tgme_channel_history'>"+body+"</div>")});
        }
    }
    [Fact]
    public async Task ActualCaptionThroughCollectorServiceDatabaseAndReplayPreservesPartsAndCursor()
    {
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"ZeroVehicleTestSettings.json")).Build();
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var collector=new TelegramCollector(config,new Page(),NullLogger<TelegramCollector>.Instance);
        var service=new ZeroVehicleService(new ZeroVehicleStore(db),collector,config);
        await service.AddChannelsAsync("fixture_channel",default);
        async Task<CarShow.ApplicationService.Contract.Dtos.ZeroVehicles.ZeroImportDto> Import() {
            var run=await service.EnqueueAsync(new("fixture_channel"),default);await service.ClaimAsync(DateTime.UtcNow,default);await service.ProcessAsync(run.Id,DateTime.UtcNow,default);return (await service.GetRunAsync(run.Id,default))!;
        }
        var first=await Import();Assert.Equal(10,first.Extracted);Assert.Equal(8,first.Stored);Assert.Equal(2,first.Rejected);
        var rows=await db.Set<ZeroVehicleListing>().OrderBy(x=>x.PartIndex).ToListAsync();
        Assert.Equal(new[]{0,2,3,4,5,6,7,8},rows.Select(x=>x.PartIndex));Assert.All(rows,x=>{Assert.Equal("901",x.ExternalMessageId);Assert.Null(x.FinalPrice);Assert.Equal("Unavailable",x.PricingStatus);});
        Assert.Equal(901,(await db.Set<ZeroVehicleChannel>().SingleAsync()).LatestMessageId);
        var replay=await Import();Assert.Equal(0,replay.Stored);Assert.Equal(8,replay.Duplicates);Assert.Equal(8,await db.Set<ZeroVehicleListing>().CountAsync());
        Assert.Empty(await db.Cars.ToListAsync());Assert.Empty(await db.CarListings.ToListAsync());
        output.WriteLine(JsonSerializer.Serialize(new {TestKind="RealCaption_ConstructedTelegramHtml_NoMarketReference_LocalSqlite",first,replay,Rows=rows.Select(x=>new{x.PartIndex,x.ExternalMessageId,x.OriginalName,x.RawPrice,x.PriceCandidates,x.PricingStatus,x.FinalPrice}),LatestMessageId=(await db.Set<ZeroVehicleChannel>().SingleAsync()).LatestMessageId}));
    }

    sealed class Unavailable(bool network) : HttpMessageHandler,IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(network)throw new HttpRequestException("Controlled transport failure");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/telegram-unavailable-20261102.html")))});
        }
    }
    [Theory]
    [InlineData(false,"PreviewUnavailable")][InlineData(true,"NetworkError")]
    public async Task ActualUnavailableHtmlIsDistinctFromTransportFailure(bool network,string reason)
    {
        var collector=new TelegramCollector(new ConfigurationBuilder().Build(),new Unavailable(network),NullLogger<TelegramCollector>.Instance);
        var result=await collector.CollectChannelAsync("baravard",default);
        Assert.False(result.Success);Assert.Empty(result.Messages);Assert.Equal(reason,result.StopReason);
    }
}
