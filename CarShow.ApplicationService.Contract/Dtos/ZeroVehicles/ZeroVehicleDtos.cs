using System.ComponentModel.DataAnnotations;
namespace CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;

public sealed record ZeroImportRequest([Required, MaxLength(128)] string Channel,
    [Range(1, 500)] int MessageLimit = 100, [Range(1, long.MaxValue)] long? Before = null);
public sealed record ZeroPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record ZeroImportDto(long Id, string Channel, string Status, int MessageLimit, long? Before,
    long? NextBefore, int PagesFetched, string StopReason, DateTime CreatedAt, DateTime? CompletedAt,
    int Scanned, int Accepted, int Rejected, int Duplicates, int Stored, int PriceGroups,
    string RejectionCounts, string? Error, int Extracted = 0, DateTime? StartedAt = null)
{
    public double? DurationSeconds => StartedAt.HasValue && CompletedAt.HasValue ? (CompletedAt.Value - StartedAt.Value).TotalSeconds : null;
}
public sealed record ZeroListingDto(long Id, long ModelId, string Model, long TrimId, string Trim,
    string OriginalName, decimal PriceToman, int? Year, DateTime PublishedAt, DateTime ReceivedAt,
    string Channel, string ExternalMessageId, string? SourceUrl, string PricingStatus, decimal? ReferencePrice, decimal? FinalPrice = null, string MarketStatus = "NotFound",
    string? MarketName = null, string? MarketUrl = null, DateTime? MarketPriceAt = null, DateTime? MarketFetchedAt = null,
    string? RejectionReason = null, int PartIndex = 0, string RawPrice = "", string? YearCalendar = null, DateOnly? PriceDay = null, string? RawCarName = null);
public sealed record ZeroListingDetail(ZeroListingDto Listing, string RawText);
public sealed record ZeroPriceDto(long Id, long ModelId, string Model, long TrimId, string Trim,
    decimal ReferencePrice, decimal MinPrice, decimal MaxPrice, int SampleCount, int SourceCount,
    string Confidence, DateTime CalculatedAt, decimal MarketPrice = 0, string PricingStatus = "Unavailable", int? Year = null, string? YearCalendar = null, DateOnly? PriceDay = null);
public sealed record ZeroCatalogDto(long Id, string Name, IReadOnlyList<ZeroTrimDto> Trims);
public sealed record ZeroTrimDto(long Id, string Name);

public sealed record ZeroChannelsRequest([Required, MaxLength(30000)] string Identifiers);
public sealed record ZeroChannelToggle(bool Enabled);
public sealed record ZeroAllImportRequest([Range(1, 500)] int MessageLimit = 100);
public sealed record ZeroChannelResult(string Channel, ZeroImportDto? Run, string? Error);
public sealed record ZeroSettingsRequest([Range(1, 50)] decimal DeviationPercent);
