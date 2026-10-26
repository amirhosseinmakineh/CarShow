namespace CarShow.Domain.Models.Telegram;

public enum TelegramChannelStatus
{
    Pending,
    Processing,
    Active,
    Paused,
    Error,
    AccessRequired,
    UnsupportedByCurrentCredentials
}

public sealed class TelegramChannel : BaseEntity<long>
{
    public string TelegramIdentifier { get; set; } = string.Empty;
    public string NormalizedIdentifier { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public TelegramChannelStatus Status { get; set; } = TelegramChannelStatus.Pending;
    public bool Enabled { get; set; } = true;
    public string CollectorType { get; set; } = "telegram";
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int MessagesCollected { get; set; }
    public int ListingsExtracted { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TelegramRawMessage> RawMessages { get; set; } = new List<TelegramRawMessage>();
}

public sealed class TelegramRawMessage : BaseEntity<long>
{
    public long ChannelId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public DateTime MessageDate { get; set; }
    public string? MediaMetadata { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
    public string ProcessingStatus { get; set; } = "Pending";
    public string? ProcessingError { get; set; }
    public TelegramChannel Channel { get; set; } = null!;
}

public sealed class CarListing : BaseEntity<long>
{
    public long? CarId { get; set; }
    public Car? Car { get; set; }
    public long? RawMessageId { get; set; }
    public TelegramRawMessage? RawMessage { get; set; }
    public bool? IsUsed { get; set; }
    public decimal? NormalizedPrice { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateOnly? PriceDay { get; set; }
    public string MatchStatus { get; set; } = "Pending";
    public string? MatchReason { get; set; }
    public string? Manufacturer { get; set; }
    public string ProcessingStatus { get; set; } = "Pending";
    public string? ComparisonStatus { get; set; }
    public string? ComparisonReason { get; set; }
    public decimal? ComparisonAveragePrice { get; set; }
    public int ComparedChannelCount { get; set; }
    public int ValidPriceSourceCount { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string? ImageUrls { get; set; }
    public string? Fingerprint { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Trim { get; set; }
    public int? Year { get; set; }
    public int? Mileage { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? City { get; set; }
    public string? BodyCondition { get; set; }
    public string? Transmission { get; set; }
    public string? Fuel { get; set; }
    public string? Color { get; set; }
    public long ChannelId { get; set; }
    public string SourceChannel { get; set; } = string.Empty;
    public string SourceMessageId { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string RawText { get; set; } = string.Empty;
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
    public string DeduplicationKey { get; set; } = string.Empty;
}
