using CarShow.Domain.Telegram;
using CarShow.BackgroundServices;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.ApplicationService.Contract.IService;
using CarShow.ApplicationService.Services;
using CarShow.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramEndpointTests
{
    [Fact]
    public async Task AdminRoutesValidateJwtBindImportAndReturnPersistedState()
    {
        await using var fixture = await TelegramCollectionTests.Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("telegram-test-key-only-never-used-by-the-production-app-12345"));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var services = builder.Services;
        services.AddControllers().AddApplicationPart(typeof(TelegramChannelsController).Assembly);
        services.AddSingleton(scope.ServiceProvider.GetRequiredService<ITelegramChannelService>());
        services.AddSingleton(new TelegramListingManagementService(
            scope.ServiceProvider.GetRequiredService<CarShow.Domain.IRepository.ITelegramProcessingStore>()));
        services.AddSingleton<ITelegramPricingQueries>(new TelegramPricingQueries(
            scope.ServiceProvider.GetRequiredService<CarShow.Domain.IRepository.ITelegramProcessingStore>()));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "telegram-tests",
                ValidAudience = "telegram-tests",
                IssuerSigningKey = key
            });
        services.AddSingleton(scope.ServiceProvider.GetRequiredService<ITelegramCollector>());
        services.AddSingleton(scope.ServiceProvider.GetRequiredService<ITelegramListingExtractor>());
        services.AddScoped<TelegramPreviewService>();
        services.AddAuthorization();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        const string baseUrl = "/api/admin/telegram-channels";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(baseUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/telegram-pricing/prices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/telegram-pricing/jobs/1/timeline")).StatusCode);
        using (var unauthenticatedClear = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/telegram-pricing/observations")
        {
            Content = JsonContent.Create(new { expectedCount = 0 })
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(unauthenticatedClear)).StatusCode);
        }
        var token = new JwtSecurityToken(
            issuer: "telegram-tests",
            audience: "telegram-tests",
            claims: [new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        foreach (var suffix in new[] { "", "/summary", "/jobs", "/capabilities" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(baseUrl + suffix)).StatusCode);
        }

        foreach (var suffix in new[] { "prices", "observations", "state" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/telegram-pricing/" + suffix)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/admin/telegram-pricing/jobs/999/timeline")).StatusCode);
        using (var invalidClear = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/telegram-pricing/observations")
        {
            Content = JsonContent.Create(new { expectedCount = -1 })
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalidClear)).StatusCode);
        }

        var invalid = await client.PostAsJsonAsync(baseUrl + "/import", new { input = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(baseUrl + "/preview", new { channels = new[] { "@example" }, messageLimit = 501 })).StatusCode);
        var preview = await client.PostAsJsonAsync(baseUrl + "/preview", new { channels = new[] { "@example" }, messageLimit = 20 });
        preview.EnsureSuccessStatusCode();
        var reports = await preview.Content.ReadFromJsonAsync<List<TelegramPreviewReport>>();
        Assert.False(Assert.Single(reports!).Success);
        Assert.Equal(0, reports![0].Received);
        var imported = await client.PostAsJsonAsync(baseUrl + "/import", new { input = "@example" });
        imported.EnsureSuccessStatusCode();
        var result = await imported.Content.ReadFromJsonAsync<BulkTelegramChannelImportResult>();
        Assert.Equal(1, result!.Created);
        Assert.Equal(1, result.Queued);
        var channels = await client.GetFromJsonAsync<List<TelegramChannelDto>>(baseUrl);
        Assert.Single(channels!);
        var jobs = await client.GetFromJsonAsync<List<TelegramJobLogDto>>(baseUrl + "/jobs");
        Assert.Equal("Queued", Assert.Single(jobs!).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/capabilities")).StatusCode);
    }
}
