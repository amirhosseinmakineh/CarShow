using System.ComponentModel.DataAnnotations;

namespace CarShow.ApplicationService.Contract.Dtos.Telegram;

public sealed record TelegramPreviewRequest(
    [Required, MinLength(1), MaxLength(10)] string[] Channels,
    [Range(1, 500)] int MessageLimit = 500,
    [Range(1, long.MaxValue)] long? Before = null);
public sealed record TelegramPreviewListing(string ChannelId, string MessageId, string? CarName, string? Trim,
    int? Year, string? YearCalendar, int? Mileage, decimal? Price, string? Currency, string? MessageUrl);
public sealed record TelegramPreviewReport(string ChannelId, int Requested, int Received, int CarAds,
    int PricesExtracted, int PagesFetched, long? NextBefore, string StopReason, long DurationMilliseconds,
    bool Success, string? Error, IReadOnlyList<TelegramPreviewListing> Listings);
