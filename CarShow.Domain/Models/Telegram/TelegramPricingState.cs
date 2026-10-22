namespace CarShow.Domain.Models.Telegram;

public sealed class TelegramPricingState : BaseEntity<long>
{
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
