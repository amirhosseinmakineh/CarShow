using System.Threading.Channels;

namespace CarShow.Infrastracture.Telegram;

public interface ITelegramCollectionQueue
{
    ValueTask EnqueueAsync(long channelId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class TelegramCollectionQueue : ITelegramCollectionQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    public ValueTask EnqueueAsync(long channelId, CancellationToken cancellationToken = default) => _channel.Writer.WriteAsync(channelId, cancellationToken);
    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
