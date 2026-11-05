namespace CarShow.Domain.Models;

/// <summary>Append-only audit for administrator decisions; raw message bodies stay immutable.</summary>
public sealed class MarketPriceReview
{
    public long Id { get; set; }
    public string Scope { get; set; } = "";
    public long TargetId { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class MarketPricePolicy
{
    // Null expiry means explicit revocation is required. Expiry never erases the audit or override.
    public static decimal Effective(decimal calculated, decimal? manual, DateTime? expiresAt, string decision, DateTime now)
        => decision == "Rejected" ? 0m : manual.HasValue && (expiresAt is null || expiresAt > now) ? manual.Value : calculated;
}
