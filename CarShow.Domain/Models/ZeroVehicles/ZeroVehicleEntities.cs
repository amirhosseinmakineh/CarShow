namespace CarShow.Domain.Models.ZeroVehicles;

public sealed class ZeroVehicleModel
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Aliases { get; set; } = "";
    public List<ZeroVehicleTrim> Trims { get; set; } = [];
}

public sealed class ZeroVehicleTrim
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public ZeroVehicleModel Model { get; set; } = null!;
    public string Name { get; set; } = "";
    public string Aliases { get; set; } = "";
}

public sealed class ZeroVehicleListing
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public ZeroVehicleModel Model { get; set; } = null!;
    public long TrimId { get; set; }
    public ZeroVehicleTrim Trim { get; set; } = null!;
    public string OriginalName { get; set; } = "";
    public decimal PriceToman { get; set; }
    public int? Year { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string Channel { get; set; } = "";
    public string ExternalMessageId { get; set; } = "";
    public string? SourceUrl { get; set; }
    public string RawText { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string PricingStatus { get; set; } = "Pending";
}

public sealed class ZeroVehiclePriceGroup
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public ZeroVehicleModel Model { get; set; } = null!;
    public long TrimId { get; set; }
    public ZeroVehicleTrim Trim { get; set; } = null!;
    public decimal ReferencePrice { get; set; }
    public decimal MinPrice { get; set; }
    public decimal MaxPrice { get; set; }
    public int SampleCount { get; set; }
    public int SourceCount { get; set; }
    public string Confidence { get; set; } = "Low";
    public DateTime CalculatedAt { get; set; }
}

// Only aggregate rejection counts are persisted. Rejected message bodies are never stored.
public sealed class ZeroVehicleImportRun
{
    public long Id { get; set; }
    public string Channel { get; set; } = "";
    public int MessageLimit { get; set; }
    public long? Before { get; set; }
    public long? NextBefore { get; set; }
    public int PagesFetched { get; set; }
    public string StopReason { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Scanned { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Duplicates { get; set; }
    public int Stored { get; set; }
    public int PriceGroups { get; set; }
    public string RejectionCounts { get; set; } = "{}";
    public string? Error { get; set; }
}
