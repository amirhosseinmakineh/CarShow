namespace CarShow.Domain.Models.ZeroVehicles;

public sealed record MarketPriceResult(string Status, string? Name = null, decimal? Price = null,
    DateTime? PriceAt = null, DateTime? FetchedAt = null, string? Url = null, string? Reason = null);
public interface ICarIrMarketPriceCrawler
{
    Task<MarketPriceResult> LookupAsync(string vehicleName, CancellationToken cancellationToken);
}
