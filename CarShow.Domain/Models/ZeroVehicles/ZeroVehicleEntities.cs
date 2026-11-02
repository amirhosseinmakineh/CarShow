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
    public int PartIndex { get; set; }
    public string RawPrice { get; set; } = "";
    public string PriceCandidates { get; set; } = "[]";
    public decimal? MarketPrice { get; set; }
    public decimal? FinalPrice { get; set; }
    public string MarketStatus { get; set; } = "NotFound";
    public string? MarketName { get; set; }
    public string? MarketUrl { get; set; }
    public DateTime? MarketPriceAt { get; set; }
    public DateTime? MarketFetchedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public sealed class ZeroVehiclePriceGroup
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public ZeroVehicleModel Model { get; set; } = null!;
    public long TrimId { get; set; }
    public ZeroVehicleTrim Trim { get; set; } = null!;
    public decimal ReferencePrice { get; set; }
    public decimal MarketPrice { get; set; }
    public string PricingStatus { get; set; } = "Unavailable";
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
    public int Extracted { get; set; }
    public int Scanned { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Duplicates { get; set; }
    public int Stored { get; set; }
    public int PriceGroups { get; set; }
    public string RejectionCounts { get; set; } = "{}";
    public string? Error { get; set; }
}

public sealed class ZeroVehicleChannel
{
    public long Id { get; set; }
    public string Identifier { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long? ScanBefore { get; set; }
    public long? PendingLatestMessageId { get; set; }
    public long? LatestMessageId { get; set; }
    public DateTime? LastImportedAt { get; set; }
    public string? LastError { get; set; }
}
public sealed class ZeroVehicleIssue
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string ExternalMessageId { get; set; } = "";
    public int PartIndex { get; set; }
    public string Code { get; set; } = "";
    public string RawText { get; set; } = "";
}
public sealed class ZeroVehicleSetting
{
    public int Id { get; set; } = 1;
    public decimal DeviationPercent { get; set; } = 15;
}
