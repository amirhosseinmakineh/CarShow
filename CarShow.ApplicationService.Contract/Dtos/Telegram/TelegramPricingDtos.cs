namespace CarShow.ApplicationService.Contract.Dtos.Telegram;

public sealed record TelegramObservationDto(
    long Id, long? CarId, long? RawMessageId, string SourceChannel, string SourceMessageId,
    string? SourceUrl, decimal? Price, string? Currency, decimal? NormalizedPrice,
    DateTime? PublishedAt, DateTime? ReceivedAt, DateOnly? PriceDay,
    string MatchStatus, string? MatchReason);

public sealed record DailyCarPriceDto(
    long CarId, string Name, decimal MarketPrice, decimal FactoryPrice,
    DateOnly? PriceDay, int SampleCount, DateTime? CalculatedAt, bool IsFinal);

public sealed record TelegramPricingStateDto(DateTime? LastSuccessAt, DateTime? LastAttemptAt, string? LastError);
