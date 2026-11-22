using CarShow.Domain.Models;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Repository;

public sealed class CarModelImageRepository(CarShowContext db)
{
    public async Task<IReadOnlyList<object>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var models = await db.CarModels.AsNoTracking().Where(x => !x.IsDelete)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ImageName })
            .ToListAsync(cancellationToken);
        return models.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> GetCarsForModelAsync(long modelId, CancellationToken cancellationToken)
    {
        var cars = await db.Cars.AsNoTracking()
            .Where(x => !x.IsDelete && x.CarModelId == modelId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.Name, x.ImageName,
                // A catalog entry may have a different row ID than the approved
                // reference record. Match the same model AND trim in Cars.
                IsReferenceApproved = x.IsReferenceApproved || (x.TipId != null
                    && db.Cars.Any(reference => !reference.IsDelete
                        && reference.IsReferenceApproved
                        && reference.CarModelId == x.CarModelId
                        && reference.TipId == x.TipId)),
                Tip = x.Tip != null ? x.Tip.Name : null
            })
            .ToListAsync(cancellationToken);
        return cars.Cast<object>().ToList();
    }

    public Task<Car?> FindCarAsync(long id, CancellationToken cancellationToken) =>
        db.Cars.FirstOrDefaultAsync(car => car.Id == id && !car.IsDelete, cancellationToken);

    public Task<CarModel?> FindModelAsync(long id, CancellationToken cancellationToken) =>
        db.CarModels.FirstOrDefaultAsync(model => model.Id == id && !model.IsDelete, cancellationToken);

    public async Task<int> SaveModelAsync(CarModel model, string imageName, Action committing,
        CancellationToken cancellationToken)
    {
        var previousImage = model.ImageName;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affectedModels = await db.CarModels
            .Where(row => row.Id == model.Id && !row.IsDelete && row.ImageName == previousImage)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ImageName, imageName), cancellationToken);
        if (affectedModels != 1)
            throw new DbUpdateConcurrencyException("The model image changed during upload.");

        // Update only approved cars that inherit the model image or have no image.
        var affectedCars = await db.Cars
            .Where(car => car.CarModelId == model.Id && car.IsReferenceApproved && !car.IsDelete
                && (car.ImageName == null || car.ImageName == ""
                    || (previousImage != null && car.ImageName == previousImage)))
            .ExecuteUpdateAsync(update => update.SetProperty(car => car.ImageName, imageName), cancellationToken);

        committing();
        await transaction.CommitAsync(cancellationToken);
        return affectedCars;
    }

    public async Task SaveCarAsync(Car car, string imageName, Action committing,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        car.ImageName = imageName;
        await db.SaveChangesAsync(cancellationToken);
        committing();
        await transaction.CommitAsync(cancellationToken);
    }
}
