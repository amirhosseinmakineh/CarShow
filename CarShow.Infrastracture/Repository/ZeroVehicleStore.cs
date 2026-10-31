using CarShow.Domain.IRepository;
using CarShow.Domain.Models.ZeroVehicles;
using CarShow.Domain.Telegram;
using CarShow.Infrastracture.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace CarShow.Infrastracture.Repository;

public sealed class ZeroVehicleStore(CarShowContext db) : IZeroVehicleStore
{
    public IQueryable<ZeroVehicleModel> Models => db.Set<ZeroVehicleModel>();
    public IQueryable<ZeroVehicleTrim> Trims => db.Set<ZeroVehicleTrim>();
    public IQueryable<ZeroVehicleListing> Listings => db.Set<ZeroVehicleListing>();
    public IQueryable<ZeroVehiclePriceGroup> Prices => db.Set<ZeroVehiclePriceGroup>();
    public IQueryable<ZeroVehicleImportRun> Runs => db.Set<ZeroVehicleImportRun>();
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public void Detach<T>(T entity) where T : class => db.Entry(entity).State = EntityState.Detached;
    public void Remove<T>(T entity) where T : class => db.Remove(entity);
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
    public async Task ExclusiveAsync(Func<Task> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsSqlServer())
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource='ZeroVehicleProcessing', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @r < 0 THROW 51000, 'Zero vehicle processing is busy.', 1;", ct);
            }
            catch (SqlException ex) when (ex.Number == 51000) { throw new TelegramProcessingBusyException("ZeroVehicleProcessing"); }
        }
        await action();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
