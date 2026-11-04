namespace CarShow.Domain.Models.Telegram;

public enum TelegramCollectionJobStatus
{
    Queued,
    Processing,
    Succeeded,
    Failed,
    AccessRequired
}

public sealed class TelegramCollectionJob : BaseEntity<long>
{
    public long ChannelId { get; set; }
    public TelegramCollectionJobStatus Status { get; set; } = TelegramCollectionJobStatus.Queued;
    public int Attempt { get; set; }
    public int ReportVersion { get; set; } = 1;
    public int NewRawMessages { get; set; }
    public int ExtractedListings { get; set; }
    public int AcceptedListings { get; set; }
    public int RejectedListings { get; set; }
    public int CalculatedPriceGroups { get; set; }
    public DateTime? PricingCompletedAt { get; set; }
    public string? RejectionReasons { get; set; }
    public string? StageErrors { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public TelegramChannel Channel { get; set; } = null!;
}
