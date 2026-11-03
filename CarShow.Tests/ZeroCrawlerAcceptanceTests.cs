using System.Net;
using System.Diagnostics;
using CarShow.Infrastracture.ZeroVehicles;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace CarShow.Tests;
public sealed class ZeroCrawlerAcceptanceTests
{
    sealed class Http(string failure="") : HttpMessageHandler,IHttpClientFactory
    {
        public int Calls,Prices;
        public HttpClient CreateClient(string name)=>new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            if(failure=="timeout")throw new TaskCanceledException("Controlled timeout");
            if(failure=="network")throw new HttpRequestException("Controlled network failure");
            if(failure=="403")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if(failure=="503")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var robots=request.RequestUri!.AbsolutePath=="/robots.txt";if(!robots)Prices++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(robots?"User-agent: *\nDisallow:":File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/carir-prices-20261102.html")))});
        }
    }
    static IConfiguration Config()=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ZeroVehicles:CarIr:MinRequestSeconds"]="1"}).Build();
    [Fact]
    public async Task ConcurrentLookupsAndCacheUseOneRealRecordedPage()
    {
        var http=new Http();var crawler=new CarIrMarketPriceCrawler(http,Config());
        var results=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>crawler.LookupAsync("آیتو M5 مدل 2025",default)));
        Assert.Equal(1,http.Prices);Assert.Equal(2,http.Calls);Assert.All(results,x=>{Assert.Equal(results[0],x);Assert.Equal(14000000000m,x.Price);});
        var fresh=await new CarIrMarketPriceCrawler(new Http(),Config()).LookupAsync("آیتو M5 مدل 2025",default);
        Assert.Equal(fresh.Name,results[0].Name);Assert.Equal(fresh.Price,results[0].Price);Assert.Equal(fresh.Status,results[0].Status);
    }
    [Theory]
    [InlineData("timeout","Timeout",1)] [InlineData("network","NetworkError",1)]
    [InlineData("403","Forbidden",1)] [InlineData("503","ServiceUnavailable",2)]
    public async Task SourceFailuresDoNotFabricatePrices(string failure,string reason,int calls)
    {
        var http=new Http(failure);var r=await new CarIrMarketPriceCrawler(http,Config()).LookupAsync("تارا",default);
        Assert.Equal("SourceUnavailable",r.Status);Assert.Equal(reason,r.Reason);Assert.Null(r.Price);Assert.Equal(calls,http.Calls);
    }
    [Fact]
    public async Task FailedRequestsAreRateLimited()
    {
        var crawler=new CarIrMarketPriceCrawler(new Http("network"),Config());var watch=Stopwatch.StartNew();
        await crawler.LookupAsync("تارا",default);await crawler.LookupAsync("شاهین",default);
        Assert.True(watch.ElapsedMilliseconds>=900);
    }
    [Theory]
    [InlineData("پژو 207","Ambiguous")][InlineData("خودروی ناشناخته","NotFound")][InlineData("تارا","Stale")]
    public void AmbiguousMissingAndOldReferenceNeverBecomeMatched(string query,string status)
    {
        var now=DateTime.UtcNow;
        var rows=new[]{new CarIrMarketPriceCrawler.Entry("پژو 207 اتومات",3e9m,now,"https://car.ir/a"),new("پژو 207 دستی",2e9m,now,"https://car.ir/b"),new("تارا",3e9m,now.AddDays(-8),"https://car.ir/c")};
        Assert.Equal(status,CarIrMarketPriceCrawler.Match(query,rows,now,7).Status);
    }
}
