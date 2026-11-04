using CarShow.ApplicationService.Services;
using CarShow.ApplicationService.Services.ZeroVehicles;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarShow.Tests;

public sealed class P0CatalogCoverageTests
{
    // Synthetic regression cases, never presented as real Telegram evidence.
    [Theory]
    [InlineData("سمند", "سمند")]
    [InlineData("دنا پلاس", "دنا+")]
    [InlineData("تارا", "تارا")]
    [InlineData("رانا پلاس", "رانا پلاس")]
    [InlineData("شاهین", "شاهین")]
    [InlineData("ساینا", "ساینا")]
    [InlineData("کوییک", "کوییک")]
    [InlineData("ام وی ام X55", "MVM X55")]
    [InlineData("فونیکس تیگو 8 پرو", "Tiggo 8 Pro")]
    [InlineData("جک S5", "جک s5")]
    [InlineData("هایما S7", "هایما s7")]
    [InlineData("دیگنیتی پرایم", "دیگنیتی")]
    [InlineData("فیدلیتی پرایم", "فیدلیتی")]
    [InlineData("لاماری ایما", "لاماری")]
    [InlineData("اکستریم VX", "Exeed VX")]
    [InlineData("تویوتا کمری", "کمری")]
    [InlineData("هیوندای توسان", "توسان")]
    [InlineData("کیا اسپورتیج", "اسپورتیج")]
    [InlineData("مرسدس بنز C200", "Mercedes C200")]
    public async Task ManagedCatalogAliasesWorkInBothPipelinesWithoutLegacyNameCollision(string canonical, string alias)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new CarShowContext(new DbContextOptionsBuilder<CarShowContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var companyName = canonical.StartsWith("مرسدس بنز", StringComparison.Ordinal) ? "بنز"
            : canonical.StartsWith("ام وی ام", StringComparison.Ordinal) ? "ام وی ام"
            : canonical is "سمند" or "دنا پلاس" or "تارا" or "رانا پلاس" ? "ایران خودرو"
            : canonical is "شاهین" or "ساینا" or "کوییک" ? "سایپا"
            : canonical.Split(' ')[0];
        var company = new Company { Name = companyName };
        db.Add(company); await db.SaveChangesAsync();
        var model = new ZeroVehicleModel { Name = canonical, Aliases = alias, CompanyId = company.Id, Trims = [
            new() { Name = "نام‌محور" }, new() { Name = "اتومات", Aliases = "اتوماتیک", Transmission = "Automatic" }] };
        db.Add(model); await db.SaveChangesAsync();
        var extractor = new TelegramListingExtractor(null, new ZeroVehicleStore(db), new TelegramProcessingStore(db));
        await extractor.LoadCatalogAsync(default);
        var now = DateTime.UtcNow;
        CarListing Used(string name, int year = 1401) => extractor.Extract(new TelegramChannel(), new TelegramRawMessage {
            ExternalMessageId = "1", MessageDate = now,
            MessageText = $"{name} اتوماتیک مدل {year} کارکرد 45.5 هزار کیلومتر قیمت 2 میلیارد تومان" })!;
        var fromAlias = Used(alias); var fromCanonical = Used(canonical);
        Assert.Equal(CarListingMatcher.Normalize(canonical), fromAlias.Model);
        Assert.Equal(fromCanonical.ComparisonKey, fromAlias.ComparisonKey);
        Assert.NotEqual(fromAlias.ComparisonKey, Used(alias, 1402).ComparisonKey);
        Assert.Equal(45500, fromAlias.Mileage);
        Assert.Equal(2_000_000_000m, fromAlias.Price);
        var zero = ZeroVehicleRules.Validate(new("1", $"{alias} اتوماتیک صفر مدل 1405 قیمت 2 میلیارد تومان", now, null, null),
            "test_dealer", [model], new(), now);
        Assert.Null(zero.RejectionCode);
        Assert.Equal(model.Id, zero.Listing!.ModelId);
        Assert.Equal(model.Trims.Single(x => x.Name == "اتومات").Id, zero.Listing.TrimId);
        Assert.Equal(1405, zero.Listing.Year);
    }
}
