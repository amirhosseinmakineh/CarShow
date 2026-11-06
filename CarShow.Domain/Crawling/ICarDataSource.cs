namespace CarShow.Domain.Crawling;

public interface ICarDataSource
{
    Task<IReadOnlyList<CarSourceData>> FetchAsync(CancellationToken cancellationToken = default);

    Task<CarSourceDetails?> FetchDetailsAsync(
        string sourceUrl,
        CancellationToken cancellationToken = default);
}
