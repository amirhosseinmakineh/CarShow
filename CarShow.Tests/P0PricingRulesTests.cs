using CarShow.ApplicationService.Services;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using Xunit;

namespace CarShow.Tests;

public sealed class P0PricingRulesTests
{
    private static CarListing Sample(long id, decimal price, int? year = 1401, string? trim = "اتومات") => new() {
        Id = id, ChannelId = id, SourceMessageId = id.ToString(), Model = "پژو 207", Trim = trim,
        IsUsed = true, Year = year, YearCalendar = year.HasValue ? "Jalali" : null, Mileage = 20000,
        ComparisonKey = CarListingMatcher.BuildComparisonKey("پژو", "پژو 207", trim, year, year.HasValue ? "Jalali" : null, true, 20000),
        PriceDay = new DateOnly(2026, 11, 4), PublishedAt = new DateTime(2026, 11, 4, 8, 0, 0, DateTimeKind.Utc),
        Price = price, NormalizedPrice = price, Currency = "IRT", Fingerprint = $"unique-{id}"
    };

    [Fact]
    public void ExactYearGroupUses920MillionAndNeverBorrowsOtherYearsOrUnknownTrim()
    {
        var groups = TelegramMarketPriceCalculator.Compare([
            Sample(1, 900_000_000m), Sample(2, 940_000_000m), Sample(3, 1_000_000_000m, 1402),
            Sample(4, 1_100_000_000m, 1403), Sample(5, 5_000_000_000m, 1401, null), Sample(6, 2_000_000_000m, null)
        ]).ToArray();
        Assert.Equal(5, groups.Length);
        Assert.Equal(920_000_000m, groups.Single(x => x.Samples.Count == 2).Average);
    }

    [Fact]
    public void RobustScreeningRetainsRawSampleButExcludesLargeOutlierWithEnoughIndependentSources()
    {
        var rows = new[] { 900m, 910m, 920m, 930m, 9_000m }.Select((price, i) => Sample(i + 1, price)).ToArray();
        var group = Assert.Single(TelegramMarketPriceCalculator.Compare(rows));
        Assert.Equal(5, group.Observations.Count);
        Assert.Equal(9000m, Assert.Single(group.Outliers).Price);
        Assert.Equal(915m, group.Average);
        Assert.Equal(9000m, rows[^1].Price);
    }

    [Fact]
    public void MadZeroUsesRelativeFloorAndSmallSamplesAreNeverAutomaticallyExcluded()
    {
        var prices = new[] { 900m, 900m, 900m, 900m, 9000m };
        Assert.Single(Assert.Single(TelegramMarketPriceCalculator.Compare(prices.Select((p, i) => Sample(i + 1, p)))).Outliers);
        Assert.Empty(Assert.Single(TelegramMarketPriceCalculator.Compare([Sample(1, 900m), Sample(2, 9000m)])).Outliers);
    }

    [Fact]
    public void LatestRepostWinsOnceAndLatestValidChannelSampleHasOneVote()
    {
        var early = Sample(1, 900m); var latest = Sample(2, 940m); latest.ChannelId = 1;
        latest.PublishedAt = early.PublishedAt!.Value.AddHours(1);
        var copy = Sample(3, 940m); copy.Fingerprint = latest.Fingerprint; copy.PublishedAt = latest.PublishedAt!.Value.AddHours(1);
        var group = Assert.Single(TelegramMarketPriceCalculator.Compare([early, latest, copy]));
        Assert.Single(group.Duplicates);
        Assert.Equal(920m, group.Average); // Channel 1 has its distinct 900 ad; newest repost has one other channel vote.
    }

    [Theory]
    [InlineData(50000, 50001)] [InlineData(100000, 100001)] [InlineData(200000, 200001)] [InlineData(300000, 300001)]
    public void MileageBoundariesRemainIndependent(int a, int b) => Assert.NotEqual(
        CarListingMatcher.BuildComparisonKey("پژو", "207", null, 1401, "Jalali", true, a),
        CarListingMatcher.BuildComparisonKey("پژو", "207", null, 1401, "Jalali", true, b));

    [Fact]
    public void PanoramaAndGearboxDoNotMixAndTrimSynonymsNormalize()
    {
        Assert.Equal(CarListingMatcher.NormalizeTrim("پانوراما اتومات"), CarListingMatcher.NormalizeTrim("اتوماتیک پانوراما"));
        var extractor = new TelegramListingExtractor();
        CarListing Parse(string name) => extractor.Extract(new TelegramChannel(), new TelegramRawMessage {
            MessageText = name + " مدل: 1401 کارکرد: 20000 قیمت: 900 میلیون تومان", ExternalMessageId = "1" })!;
        Assert.NotEqual(Parse("207 پانوراما دنده‌ای").ComparisonKey, Parse("207 دنده‌ای").ComparisonKey);
        Assert.NotEqual(Parse("207 اتومات").ComparisonKey, Parse("207 دنده‌ای").ComparisonKey);
        Assert.Equal(Parse("207 اتومات").ComparisonKey, Parse("پژو 207 اتوماتیک").ComparisonKey);
    }

    [Fact]
    public void ManualPriorityExpiresOrRevokesWithoutErasingCalculatedPrice()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(1000m, MarketPricePolicy.Effective(920m, 1000m, null, "Unreviewed", now));
        Assert.Equal(1000m, MarketPricePolicy.Effective(920m, 1000m, now.AddHours(1), "Approved", now));
        Assert.Equal(920m, MarketPricePolicy.Effective(920m, 1000m, now.AddSeconds(-1), "Approved", now));
        Assert.Equal(920m, MarketPricePolicy.Effective(920m, null, null, "Approved", now));
        Assert.Equal(0m, MarketPricePolicy.Effective(920m, 1000m, null, "Rejected", now));
    }

    [Theory]
    [InlineData("HTTP://T.ME/AutoPalermo/", "autopalermo")]
    [InlineData("@AutoPalermo", "autopalermo")]
    [InlineData("https://t.me/+kF0Dpp9cMl45MjNk", "+kF0Dpp9cMl45MjNk")]
    public void SourcesNormalizePublicIdentitiesAndPreserveCaseSensitivePrivateInvite(string input, string expected)
        => Assert.Equal(expected, TelegramChannelService.Normalize(input)!.Value.id);
    [Theory]
    [InlineData("207 اتومات 1401 کارکرد 20000 قیمت 900م", 1401, "Jalali")]
    [InlineData("تارا 2025 صفر قیمت 2950م", 2025, "Gregorian")]
    [InlineData("تارا صفر قیمت 1405م", null, null)]
    [InlineData("تارا صفر بیمه تا 1405 قیمت 2950م", null, null)]
    [InlineData("تارا صفر بیمه تا سال 1405 قیمت 2950م", null, null)]
    [InlineData("تارا مدل 1401 صفر قیمت 1405م", 1401, "Jalali")]
    public void ModelYearIsExplicitAndNeverBorrowedFromPriceOrInsurance(string text, int? expected, string? calendar)
    {
        var result = VehicleModelYear.Read(text);
        Assert.Equal(expected, result.Year); Assert.Equal(calendar, result.Calendar);
    }

    [Fact]
    public void OversizedSecondaryAmountCannotOverflowOrPublishAndUnitErrorsNeedReview()
    {
        const string text = "پژو 207 اتومات مدل 1401 کارکرد 20000 قیمت 1 میلیارد و 99999999999999999999999 میلیون تومان";
        var row = new TelegramListingExtractor().Extract(new TelegramChannel(), new TelegramRawMessage { ExternalMessageId = "1", MessageText = text });
        Assert.NotNull(row); Assert.Null(row.Price);
        Assert.Empty(CarShow.ApplicationService.Services.ZeroVehicles.ZeroVehicleRules.ExtractPrice(text,
            new CarShow.ApplicationService.Services.ZeroVehicles.ZeroVehicleOptions()).Candidates);
        var tiny = Sample(1, 1_000_000m);
        Assert.Equal("PRICE_UNIT_OR_RANGE_REVIEW", TelegramMarketPriceCalculator.ValidationFailure(tiny, tiny.PublishedAt!.Value)!.Value.Code);
    }

    [Theory]
    [InlineData("کارکرد 150.000", null)]
    [InlineData("کارکرد 1.5 هزار", 1500)]
    [InlineData("150 هزار کیلومتر لاستیک صفر", 150000)]
    public void MileageFormattingNeverSilentlySelectsTheWrongBucket(string mileageText, int? expected)
    {
        var row = new TelegramListingExtractor().Extract(new TelegramChannel(), new TelegramRawMessage {
            ExternalMessageId = "1", MessageText = "پژو 207 اتومات مدل 1401 " + mileageText + " قیمت 900 میلیون تومان" })!;
        Assert.Equal(expected, row.Mileage); Assert.Equal(true, row.IsUsed);
        if (expected is null) Assert.EndsWith("|unknown", row.ComparisonKey);
    }

}
