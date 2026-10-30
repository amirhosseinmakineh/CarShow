namespace CarShow.ApplicationService.Contract.Dtos.Telegram;

public sealed record TelegramObservationDto(
    long Id, long? CarId, long? RawMessageId, string SourceChannel, string SourceMessageId,
    string? SourceUrl, decimal? Price, string? Currency, decimal? NormalizedPrice,
    DateTime? PublishedAt, DateTime? ReceivedAt, DateOnly? PriceDay,
    string MatchStatus, string? MatchReason, string? Manufacturer = null, string? PrimaryImageUrl = null,
    string? ComparisonStatus = null, decimal? ComparisonAveragePrice = null, int ComparedChannelCount = 0,
    int ValidPriceSourceCount = 0, string ProcessingStatus = "Pending", string? ReasonCode = null,
    int? Year = null, int? Mileage = null, bool? IsUsed = null, string? Trim = null,
    string? Model = null, string? PriceText = null, string? YearCalendar = null,
    string? ComparisonReason = null, int ExtractionVersion = 0, int RegisteredChannelCount = 0,
    string? RawCarName = null, string ExtractionStatus = "Extracted",
    string NameValidationStatus = "NeedsReview", string PriceValidationStatus = "NeedsReview");

public sealed record DailyCarPriceDto(
    long CarId, string Name, decimal MarketPrice, decimal FactoryPrice,
    DateOnly? PriceDay, int SampleCount, DateTime? CalculatedAt, bool IsFinal,
    int IndependentSourceCount, string ConfidenceLevel);

public sealed record TelegramPricingStateDto(DateTime? LastSuccessAt, DateTime? LastAttemptAt, string? LastError);

public sealed record TelegramTimelineStageDto(string Code, string Status, DateTime? OccurredAt, int Count);
public sealed record TelegramJobTimelineDto(long JobId, long ChannelId, string JobStatus,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? CompletedAt, string? Error,
    IReadOnlyList<TelegramTimelineStageDto> Stages);
public sealed record TelegramListingClearRequest(int ExpectedCount);
public sealed record TelegramListingClearResult(int ClearedCount, int PreservedRawMessages);

public sealed record TelegramPipelineTimelineDto(
    string Status, DateTime? StartedAt, DateTime? CompletedAt,
    int TotalChannels, int CompletedChannels, int FailedChannels,
    IReadOnlyList<TelegramTimelineStageDto> Stages);
