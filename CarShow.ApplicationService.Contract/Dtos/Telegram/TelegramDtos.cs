using System.ComponentModel.DataAnnotations;

namespace CarShow.ApplicationService.Contract.Dtos.Telegram;

public sealed record BulkTelegramChannelImportRequest(
    [Required, StringLength(100000)] string Input);
public sealed record BulkTelegramChannelEnabledRequest([Required] long[] ChannelIds, bool Enabled);
public sealed record UpdateTelegramChannelRequest([Required, StringLength(256)] string Input, [StringLength(512)] string Title);
public sealed record AddTelegramChannelRequest(
    [Required, StringLength(256)] string Input);
public sealed record TelegramChannelDto(
    long Id,
    string Identifier,
    string Username,
    string? PublicUrl,
    string Title,
    string Status,
    bool Enabled,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    string? LastError,
    int MessagesCollected,
    int ListingsExtracted,
    int RetryCount,
    int ProcessedListings = 0,
    int FailedListings = 0,
    int NeedsReviewListings = 0,
    int WaitingComparisonListings = 0,
    int ValidPriceListings = 0,
    int IgnoredMessages = 0,
    int ValidNameListings = 0,
    int ComparedListings = 0,
    int ReferencePriceGroups = 0,
    DateTime? LastCalculationAt = null);
public sealed record TelegramChannelSummaryDto(
    int TotalChannels,
    int Active,
    int Paused,
    int Errors,
    int Pending,
    int Processing,
    int MessagesCollected,
    int ListingsExtracted,
    DateTime? LastCollection,
    int NewCars = 0,
    int UsedCars = 0,
    int ComparedCars = 0,
    int PendingListings = 0,
    int RejectedListings = 0,
    int MissingImageListings = 0,
    int MissingManufacturerListings = 0,
    int IgnoredMessages = 0,
    int ValidPriceListings = 0,
    int StoredListingsIncludingRemovedChannels = 0);
public sealed record BulkTelegramChannelImportResult(
    int Total,
    int Created,
    int Duplicates,
    int Invalid,
    int Queued,
    IReadOnlyList<string> InvalidEntries);

public sealed record TelegramJobLogDto(
    long Id,
    long ChannelId,
    string Status,
    int Attempt,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? Error, DateTime? StartedAt = null, int NewRawMessages = 0, int ExtractedListings = 0,
    int AcceptedListings = 0, int RejectedListings = 0, int CalculatedPriceGroups = 0,
    DateTime? PricingCompletedAt = null, string? RejectionReasons = null, string? StageErrors = null, int ReportVersion = 0)
{
    public double? DurationSeconds => StartedAt.HasValue && CompletedAt.HasValue ? (CompletedAt.Value - StartedAt.Value).TotalSeconds : null;
}

public sealed record TelegramCapabilityDto(
    string Provider,
    bool PublicPreviewEnabled,
    bool RequiresApiCredentials,
    bool SupportsPublicUsernames,
    bool SupportsHistory,
    string Message);
