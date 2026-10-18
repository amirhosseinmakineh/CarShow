namespace CarShow.Domain.IRepository;

public interface ITelegramCollectionQueue
{
    ValueTask EnqueueAsync(long channelId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken);
}
