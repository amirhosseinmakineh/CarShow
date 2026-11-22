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

    public async Task<(string? ImageName, int UpdatedCars)> SaveAsync(
        long modelId, Stream imageStream, string extension, CancellationToken cancellationToken)
    {
        var model = await db.CarModels.FirstOrDefaultAsync(x => x.Id == modelId && !x.IsDelete, cancellationToken);
        if (model is null) return (null, 0);

        var directory = CarService.GetImageDirectory();
        Directory.CreateDirectory(directory);
        var fileName = $"model-{modelId}-{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, fileName);
        await using (var output = new FileStream(fullPath, FileMode.CreateNew))
            await imageStream.CopyToAsync(output, cancellationToken);

        var previousImage = model.ImageName;
        var newImage = $"/images/cars/{fileName}";
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
            File.Delete(fullPath);
            throw;
        }
        return (newImage, affectedCars.Count);
    }
}
