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
    string? ComparisonReason = null, int ExtractionVersion = 0, int RegisteredChannelCount = 0);

public sealed record DailyCarPriceDto(
    long CarId, string Name, decimal MarketPrice, decimal FactoryPrice,
    DateOnly? PriceDay, int SampleCount, DateTime? CalculatedAt, bool IsFinal);

public sealed record TelegramPricingStateDto(DateTime? LastSuccessAt, DateTime? LastAttemptAt, string? LastError);
