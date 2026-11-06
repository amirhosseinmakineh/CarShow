namespace CarShow.Domain.Crawling;

public interface ICarCrawlerSyncService
{
    Task<int> SyncAsync(CancellationToken cancellationToken = default);
}
