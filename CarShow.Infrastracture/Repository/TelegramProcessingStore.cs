using CarShow.Domain.IRepository;
using CarShow.Domain.Telegram;
using Microsoft.Data.SqlClient;
using CarShow.Domain.Models;
using CarShow.Domain.Models.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Repository;

public sealed class TelegramProcessingStore(CarShowContext db) : ITelegramProcessingStore
{
    public IQueryable<TelegramChannel> Channels => db.TelegramChannels;
    public IQueryable<TelegramCollectionJob> Jobs => db.TelegramCollectionJobs;
    public IQueryable<TelegramRawMessage> Messages => db.TelegramRawMessages;
    public IQueryable<CarListing> Listings => db.CarListings;
    public IQueryable<TelegramPricingState> PricingStates => db.Set<TelegramPricingState>();
    public IQueryable<Car> Cars => db.Cars.Include(x => x.Company).Include(x => x.CarModel).Include(x => x.Tip);
    public IQueryable<Company> Companies => db.Companies;
    public IQueryable<CarModel> CarModels => db.CarModels;
    public IQueryable<Tip> Tips => db.Tips;
    public IQueryable<CarPriceHistory> History => db.CarPriceHistories;

    public void Add<T>(T entity) where T : class
    {
        db.Add(entity);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RunExclusiveAsync(string resource, Func<Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (db.Database.IsSqlServer())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                        @LockOwner='Transaction', @LockTimeout=0;
                    IF @result < 0 THROW 51000, 'Telegram processing is already running.', 1;
                    """, cancellationToken);
                if (resource is "TelegramDailyPricing" or "TelegramEnqueue")
                    await db.Database.ExecuteSqlRawAsync("DECLARE @s int; EXEC @s = sp_getapplock @Resource='TelegramPricingState', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @s < 0 THROW 51000, 'Pricing state is busy.', 1;", cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 51000)
            {
                throw new TelegramProcessingBusyException(resource);
            }
        }

        await action();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
