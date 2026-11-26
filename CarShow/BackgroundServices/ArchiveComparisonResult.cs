namespace CarShow.BackgroundServices;

public sealed record ArchiveComparisonResult
{
    public string? CompanyName { get; init; }
    public string Model { get; init; } = string.Empty;
    public string Trim { get; init; } = string.Empty;
    public int Year { get; init; }
    public int PersianYear { get; init; }
    public int TotalCount { get; init; }
    public int ValidCount { get; init; }
    public int RemovedCount { get; init; }
    public decimal MedianPriceBillion { get; init; }
    public decimal? MinPriceBillion { get; init; }
    public decimal? MaxPriceBillion { get; init; }
    public decimal? ReferencePriceBillion { get; init; }
    public long? ReferencePriceToman { get; init; }
    public string Status { get; init; } = string.Empty;
}
