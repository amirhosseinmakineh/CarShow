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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public TelegramChannel Channel { get; set; } = null!;
}
