using CarShow.ApplicationService.Services;
using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Application;

public sealed class CarModelImageService(CarShowContext db)
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
            .Select(x => new { x.Id, x.Name, x.ImageName, x.IsReferenceApproved,
                Tip = x.Tip != null ? x.Tip.Name : null })
            .ToListAsync(cancellationToken);
        return cars.Cast<object>().ToList();
    }

    public async Task<(string? ImageName, bool UpdatedReference)> SaveCarAsync(
        long carId, Stream imageStream, string extension, CancellationToken cancellationToken)
    {
        var car = await db.Cars.FirstOrDefaultAsync(x => x.Id == carId && !x.IsDelete, cancellationToken);
        if (car is null) return (null, false);
        var path = await SaveFileAsync($"car-{carId}", imageStream, extension, cancellationToken);
        car.ImageName = path;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { File.Delete(ToFullPath(path)); throw; }
        return (path, car.IsReferenceApproved);
    }

    private static string ToFullPath(string imagePath) =>
        Path.Combine(CarService.GetImageDirectory(), Path.GetFileName(imagePath));

    private static async Task<string> SaveFileAsync(string prefix, Stream input, string extension,
        CancellationToken cancellationToken)
    {
        var directory = CarService.GetImageDirectory();
        Directory.CreateDirectory(directory);
        var name = $"{prefix}-{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, name);
        await using (var output = new FileStream(path, FileMode.CreateNew))
            await input.CopyToAsync(output, cancellationToken);
        return $"/images/cars/{name}";
    }

    public async Task<(string? ImageName, int UpdatedCars)> SaveAsync(
        long modelId, Stream imageStream, string extension, CancellationToken cancellationToken)
    {
        var model = await db.CarModels.FirstOrDefaultAsync(x => x.Id == modelId && !x.IsDelete, cancellationToken);
        if (model is null) return (null, 0);

        var newImage = await SaveFileAsync($"model-{modelId}", imageStream, extension, cancellationToken);

        var previousImage = model.ImageName;
        model.ImageName = newImage;

        // Do not replace images uploaded specifically for individual cars.
        var affectedCars = await db.Cars
            .Where(x => x.CarModelId == modelId && x.IsReferenceApproved && !x.IsDelete
                && (x.ImageName == "" || x.ImageName == previousImage))
            .ToListAsync(cancellationToken);
        foreach (var car in affectedCars) car.ImageName = newImage;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            File.Delete(ToFullPath(newImage));
            throw;
        }
        return (newImage, affectedCars.Count);
    }
}
