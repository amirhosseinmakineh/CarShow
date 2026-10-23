using System.Net;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarShow.Tests;

public class TelegramPreviewTests
{
    [Fact]
    public async Task PaginatesTo500WithoutDuplicatesAndReturnsResumeCursor()
    {
        var handler = new Pages();
        var collector = Collector(handler);
        var result = await collector.CollectChannelAsync("@example", 500, null, default);
        Assert.True(result.Success);
        Assert.Equal(500, result.Messages.Count);
        Assert.Equal(500, result.Messages.Select(m => m.ExternalMessageId).Distinct().Count());
        Assert.Equal(501, result.NextBefore);
        Assert.Equal(25, result.PagesFetched);
        Assert.Equal("RequestedLimitReached", result.StopReason);
        var next = await collector.CollectChannelAsync("example", 20, result.NextBefore, default);
        Assert.DoesNotContain(next.Messages, m => result.Messages.Any(p => p.ExternalMessageId == m.ExternalMessageId));
    }

    [Fact]
    public async Task ReportsPartialMessagesWhenNextPageFails()
    {
        var result = await Collector(new Pages(true)).CollectChannelAsync("example", 500, null, default);
        Assert.False(result.Success);
        Assert.Equal(20, result.Messages.Count);
        Assert.Equal("HttpError", result.StopReason);
        Assert.Contains("503", result.Error);
    }

    [Theory]
    [InlineData("-100123456")]
    [InlineData("https://evil.test/")]
    [InlineData("../admin")]
    public async Task RejectsNonPublicUsernamesWithoutHttp(string input)
    {
        var handler = new Pages();
        var result = await Collector(handler).CollectChannelAsync(input, 500, null, default);
        Assert.Equal("InvalidInput", result.StopReason);
        Assert.Equal(0, handler.Calls);
    }

    private static TelegramCollector Collector(Pages handler) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Telegram:RequestDelayMilliseconds"]="0" }).Build(),
        new Factory(handler), NullLogger<TelegramCollector>.Instance);
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class Pages(bool failSecond = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (failSecond && Calls > 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var query = request.RequestUri!.Query;
            var before = string.IsNullOrEmpty(query) ? 1001 : int.Parse(query.Split('=')[1]);
            var html = "<div class='tgme_channel_history'>" + string.Concat(Enumerable.Range(Math.Max(1,before-20),Math.Min(20,before-1)).Select(id => $"<div data-post='example/{id}'><div class='tgme_widget_message_text'>خودرو: دنا پلاس<br>قیمت: 1300 میلیون تومان</div><time datetime='2026-10-23T00:00:00Z'></time></div>")) + "</div>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }
}
