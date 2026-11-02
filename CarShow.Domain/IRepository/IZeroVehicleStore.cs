using CarShow.Domain.Models.ZeroVehicles;
namespace CarShow.Domain.IRepository;

public interface IZeroVehicleStore
{
    IQueryable<ZeroVehicleChannel> Channels { get; }
    IQueryable<ZeroVehicleIssue> Issues { get; }
    IQueryable<ZeroVehicleSetting> Settings { get; }
    IQueryable<ZeroVehicleModel> Models { get; }
    IQueryable<ZeroVehicleTrim> Trims { get; }
    IQueryable<ZeroVehicleListing> Listings { get; }
    IQueryable<ZeroVehiclePriceGroup> Prices { get; }
    IQueryable<ZeroVehicleImportRun> Runs { get; }
    void Add<T>(T entity) where T : class;
    void Detach<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken cancellationToken);
    Task ExclusiveAsync(Func<Task> action, CancellationToken cancellationToken);
}
