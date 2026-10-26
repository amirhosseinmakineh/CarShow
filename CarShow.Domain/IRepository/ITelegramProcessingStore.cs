using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;

namespace CarShow.Domain.IRepository;

public interface ITelegramProcessingStore
{
    IQueryable<TelegramChannel> Channels { get; }
    IQueryable<TelegramCollectionJob> Jobs { get; }
    IQueryable<TelegramRawMessage> Messages { get; }
    IQueryable<CarListing> Listings { get; }
    IQueryable<TelegramPricingState> PricingStates { get; }
    IQueryable<Car> Cars { get; }
    IQueryable<Company> Companies { get; }
    IQueryable<CarModel> CarModels { get; }
    IQueryable<Tip> Tips { get; }
    IQueryable<CarPriceHistory> History { get; }
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken cancellationToken);
    Task RunExclusiveAsync(string resource, Func<Task> action, CancellationToken cancellationToken);
}
