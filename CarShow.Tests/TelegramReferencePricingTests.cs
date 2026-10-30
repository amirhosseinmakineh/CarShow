using CarShow.ApplicationService.Services;
using CarShow.Domain.Models.Telegram;
using Xunit;

namespace CarShow.Tests;

public sealed class TelegramReferencePricingTests
{
    private static CarListing Extract(string text) => new TelegramListingExtractor().Extract(
        new TelegramChannel { NormalizedIdentifier = "@sample" },
        new TelegramRawMessage
        {
            Id = 1,
            ExternalMessageId = "42",
            SourceUrl = "https://t.me/sample/42",
            MessageText = text,
            MessageDate = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc)
        })!;

    [Theory]
    [InlineData("پژو 207 مدل: 1403 کارکرد: ۰ قیمت: 850 میلیون تومان", false)]
    [InlineData("پژو 207 مدل: 1403 بدون کارکرد قیمت: 850 میلیون تومان", false)]
    [InlineData("پژو 207 مدل: 1403 صفر کیلومتر قیمت: 850 میلیون تومان", false)]
    [InlineData("پژو 207 مدل: 1403 کارکرد: ۵۰٬۰۰۰ قیمت: 850 میلیون تومان", true)]
    [InlineData("پژو 207 مدل: 1403 کارکرد: ۲۰۰ هزار قیمت: 850 میلیون تومان", true)]
    public void MissingTrimOrImageDoesNotRejectValidPrice(string text, bool used)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, []);
        listing.PriceDay = new DateOnly(2026, 10, 29);
        Assert.Equal(used, listing.IsUsed);
        Assert.NotNull(listing.ComparisonKey);
        Assert.Null(listing.Trim);
        Assert.Null(listing.PrimaryImageUrl);
        Assert.Null(TelegramMarketPriceCalculator.ValidationFailure(listing,
            new DateTime(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 29)));
    }

    [Fact]
    public void AmbiguousMileageAndExplicitUsedDoNotBecomeNew()
    {
        Assert.Null(Extract("پژو 207 مدل: 1403 قیمت: 850 میلیون تومان").IsUsed);
        Assert.NotEqual(false, Extract("پژو 207 مدل: 1403 کارکرده کارکرد: 0 قیمت: 850 میلیون تومان").IsUsed);
    }

    [Fact]
    public void AliasesShareIdentityButDifferentTrimsAndMileageRangesDoNot()
    {
        var a = Extract("پژو پارس XU7 مدل: 1403 صفر قیمت: 850 میلیون تومان");
        var b = Extract("پارس سال XU7 مدل: 1403 بدون کارکرد قیمت: 850 میلیون تومان");
        Assert.Equal(a.ComparisonKey, b.ComparisonKey);
        Assert.NotEqual(CarListingMatcher.BuildComparisonKey("پژو", "پارس", "دستی", 1403, "Jalali", false), a.ComparisonKey);
        Assert.NotEqual(CarListingMatcher.BuildComparisonKey("پژو", "207", null, 1403, "Jalali", true, 50_000),
            CarListingMatcher.BuildComparisonKey("پژو", "207", null, 1403, "Jalali", true, 50_001));
    }

    [Fact]
    public void SingleSourceAndMultipleSourcesUseArithmeticMean()
    {
        Assert.Equal(850m, TelegramMarketPriceCalculator.ReferencePrice([850m]));
        Assert.Equal(900m, TelegramMarketPriceCalculator.ReferencePrice([850m, 950m]));
        Assert.Equal(3175m, TelegramMarketPriceCalculator.ReferencePrice([850m, 900m, 950m, 10_000m]));
    }

    [Theory]
    [InlineData("دنا+ توربو مدل: 1403 بدون کارکرد قیمت: ۹۸۰ میلیون تومان", "دنا پلاس", 980000000)]
    [InlineData("پژو ۲۰۷ اتوماتیک مدل: 1403 کارکرد: 20000 قیمت: 980 میلیون", "پژو 207", 980000000)]
    [InlineData("شاهین G مدل: 1403 بدون کارکرد قیمت: ۱ میلیارد و ۱۵۰ میلیون تومان", "شاهین", 1150000000)]
    [InlineData("KMC J7 مدل: 1403 بدون کارکرد قیمت نقدی: ۹۸۰,۰۰۰,۰۰۰", "kmc j7", 980000000)]
    [InlineData("پژو 207 مدل: 1403 بدون کارکرد قیمت: ۹,۸۰۰,۰۰۰,۰۰۰ ریال", "پژو 207", 980000000)]
    [InlineData("۲۰۷ اتوماتیک مدل: 1403 بدون کارکرد قیمت: 980 میلیون", "پژو 207", 980000000)]
    public void CanonicalNameAndExplicitPriceWithoutManufacturer(string text, string model, decimal expected)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, []);
        Assert.Equal(model, listing.Model);
        Assert.Equal(expected, listing.NormalizedPrice);
        if (!text.Contains("پژو", StringComparison.Ordinal))
            Assert.Null(listing.Manufacturer);
        Assert.NotNull(listing.ComparisonKey);
    }

    [Theory]
    [InlineData("پژو 207 مدل: 1403 صفر قیمت: 980")]
    [InlineData("پژو 207 مدل: 1403 صفر قیمت: توافقی")]
    [InlineData("پژو 207 مدل: 1403 صفر پیش پرداخت: 300 میلیون قیمت: 980 میلیون تومان")]
    public void AmbiguousOrInstallmentPricesDoNotEnterPricing(string text)
    {
        var listing = Extract(text);
        CarListingMatcher.Match(listing, []);
        Assert.Null(listing.NormalizedPrice);
        Assert.Equal("NeedsReview", listing.ProcessingStatus);
    }

    [Fact]
    public void MultipleCarsRequireReviewAndRawTextIsPreserved()
    {
        const string text = "پژو 207 و شاهین مدل: 1403 صفر قیمت: 980 میلیون تومان";
        var listing = Extract(text);
        CarListingMatcher.Match(listing, []);
        Assert.Equal("CAR_NAME_AMBIGUOUS", listing.ReasonCode);
        Assert.Equal(text, listing.RawText);
    }

    [Fact]
    public void ExtremePriceIsRetainedForReviewButExcludedFromArithmeticMean()
    {
        var listings = new[] { 800m, 900m, 9000m }.Select((price, index) => new CarListing
        {
            Id = index + 1,
            ChannelId = index + 1,
            SourceMessageId = (index + 1).ToString(),
            ComparisonKey = "same-car",
            PriceDay = new DateOnly(2026, 10, 29),
            NormalizedPrice = price
        }).ToArray();
        var group = Assert.Single(TelegramMarketPriceCalculator.Compare(listings));
        Assert.Equal(850m, group.Average);
        Assert.Single(group.Outliers);
        Assert.Equal(9000m, group.Outliers[0].NormalizedPrice);
    }
}
