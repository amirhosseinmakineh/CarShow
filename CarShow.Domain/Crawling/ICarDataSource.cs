namespace CarShow.Domain.Crawling;

public interface ICarDataSource
{
    Task<IReadOnlyList<CarSourceData>> FetchAsync(CancellationToken cancellationToken = default);
}
