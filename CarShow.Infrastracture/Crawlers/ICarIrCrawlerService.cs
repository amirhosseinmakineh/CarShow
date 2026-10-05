namespace CarShow.Infrastracture.Crawlers
{
    public interface ICarIrCrawlerService
    {
        Task<int> SyncAsync(CancellationToken cancellationToken = default);
    }
}
