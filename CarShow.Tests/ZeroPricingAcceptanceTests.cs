using System.Text;
using System.Text.Json;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;
namespace CarShow.Tests;
public sealed class ZeroPricingAcceptanceTests(ITestOutputHelper output)
{
    sealed class Collector : ITelegramCollector
    {
        public string Text="";
        public Task<TelegramCollectionResult> CollectChannelAsync(string channel,CancellationToken ct)=>Task.FromResult(new TelegramCollectionResult(true,false,[new("100",Text,DateTime.UtcNow,null,null)],null));
    }
    sealed class Market(string status) : ICarIrMarketPriceCrawler
    {
        public Task<MarketPriceResult> LookupAsync(string name,CancellationToken ct)=>Task.FromResult(new MarketPriceResult(status,"تارا",3_000_000_000m,DateTime.UtcNow,DateTime.UtcNow,"https://car.ir/fixture"));
    }
    [Theory]
    [InlineData("A","Matched",2975000000,1)]
    [InlineData("B","Matched",3000000000,0)]
    [InlineData("C","Matched",3000000000,0)]
    [InlineData("D","Matched",3000000000,0)]
    [InlineData("E","NotFound",0,0)]
    [InlineData("E","Ambiguous",0,0)]
    [InlineData("E","Stale",0,0)]
    [InlineData("E","SourceUnavailable",0,0)]
    [InlineData("F","Matched",3000000000,3)]
    [InlineData("G","Matched",2950000000,1)]
    [InlineData("H","Matched",2975000000,1)]
    public async Task PricingScenarios(string scenario,string marketStatus,long expectedFinal,int expectedSamples)
    {
        var inputs=scenario switch {
            "A"=>new[]{"تارا صفر ۲۹۵۰م"},
            "B" or "D"=>["تارا صفر ۱۲۰۰م"],
            "C"=>["تارا صفر ۴۰۰۰م"],
            "F"=>["تارا صفر ۲۹۰۰م تهران","تارا صفر ۳۱۰۰م مشهد","تارا صفر ۳۰۰۰م شیراز"],
            "G"=>["تارا صفر ۲۹۰۰م","تارا صفر ۲۹۰۰م","تارا صفر ۲۹۰۰م"],
            "H"=>["تارا بدون کارکرد ۲۹۵۰م\nکارت طلایی ۲۴م تسویه شد"],
            _=>new[]{"تارا صفر ۲۹۵۰م"}};
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);await db.Database.EnsureCreatedAsync();
        var config=new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""{"ZeroVehicles":{"Catalog":[{"Name":"تارا","Aliases":[],"Trims":[]}]}}"""))).Build();
        var collector=new Collector();var service=new ZeroVehicleService(new ZeroVehicleStore(db),collector,config,new Market(marketStatus));
        for(var i=0;i<inputs.Length;i++) {
            collector.Text=inputs[i];var run=await service.EnqueueAsync(new("channel_"+i),default);await service.ClaimAsync(DateTime.UtcNow,default);await service.ProcessAsync(run.Id,DateTime.UtcNow,default);
        }
        var rows=await db.Set<ZeroVehicleListing>().ToListAsync();var group=await db.Set<ZeroVehiclePriceGroup>().SingleOrDefaultAsync();
        output.WriteLine(JsonSerializer.Serialize(new {scenario,marketStatus,inputs,ExpectedFinal=expectedFinal==0?(decimal?)null:expectedFinal,ActualFinal=group?.ReferencePrice,ExpectedSamples=expectedSamples,ActualSamples=group?.SampleCount??0,GroupStatus=group?.PricingStatus??"Unavailable",Rows=rows.Select(x=>new{x.Channel,x.PriceToman,x.PricingStatus,x.FinalPrice})}));
        Assert.Equal(inputs.Length,rows.Count);
        if(expectedFinal==0) { Assert.Null(group);Assert.All(rows,x=>{Assert.Equal("Unavailable",x.PricingStatus);Assert.Null(x.FinalPrice);}); }
        else { Assert.NotNull(group);Assert.Equal((decimal)expectedFinal,group.ReferencePrice);Assert.Equal(expectedSamples,group.SampleCount);Assert.Equal(expectedSamples==0?"ReferenceOnly":"Priced",group.PricingStatus); }
        if(scenario=="G")Assert.Equal(2,rows.Count(x=>x.PricingStatus=="DuplicateContent"));
        if(scenario is "B" or "C" or "D")Assert.Equal("Outlier",Assert.Single(rows).PricingStatus);
        Assert.Empty(await db.Cars.ToListAsync());Assert.Empty(await db.CarListings.ToListAsync());
    }
}
