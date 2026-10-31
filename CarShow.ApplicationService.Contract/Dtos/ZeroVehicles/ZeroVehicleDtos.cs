using System.ComponentModel.DataAnnotations;
namespace CarShow.ApplicationService.Contract.Dtos.ZeroVehicles;

public sealed record ZeroImportRequest([Required, MaxLength(128)] string Channel,
    [Range(1, 500)] int MessageLimit = 100, [Range(1, long.MaxValue)] long? Before = null);
public sealed record ZeroPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record ZeroImportDto(long Id, string Channel, string Status, int MessageLimit, long? Before,
    long? NextBefore, int PagesFetched, string StopReason, DateTime CreatedAt, DateTime? CompletedAt,
    int Scanned, int Accepted, int Rejected, int Duplicates, int Stored, int PriceGroups,
    string RejectionCounts, string? Error);
public sealed record ZeroListingDto(long Id, long ModelId, string Model, long TrimId, string Trim,
    string OriginalName, decimal PriceToman, int? Year, DateTime PublishedAt, DateTime ReceivedAt,
    string Channel, string ExternalMessageId, string? SourceUrl, string PricingStatus, decimal? ReferencePrice);
public sealed record ZeroListingDetail(ZeroListingDto Listing, string RawText);
public sealed record ZeroPriceDto(long Id, long ModelId, string Model, long TrimId, string Trim,
    decimal ReferencePrice, decimal MinPrice, decimal MaxPrice, int SampleCount, int SourceCount,
    string Confidence, DateTime CalculatedAt);
public sealed record ZeroCatalogDto(long Id, string Name, IReadOnlyList<ZeroTrimDto> Trims);
public sealed record ZeroTrimDto(long Id, string Name);
