#nullable enable
using CarShow.ApplicationService.Services;
using CarShow.Diagnostics;
using CarShow.Infrastracture.Repository;

namespace CarShow.Application;

public sealed class CarModelImageService(
    CarModelImageRepository repository,
    UploadDiagnostics diagnostics,
    IConfiguration configuration,
    ILogger<CarModelImageService> logger)
{
    private readonly string directory = CarService.GetImageDirectory(configuration);

    public Task<IReadOnlyList<object>> GetModelsAsync(CancellationToken cancellationToken) =>
        repository.GetModelsAsync(cancellationToken);

    public Task<IReadOnlyList<object>> GetCarsForModelAsync(long modelId, CancellationToken cancellationToken) =>
        repository.GetCarsForModelAsync(modelId, cancellationToken);

    public async Task<(string? ImageName, bool UpdatedReference)> SaveCarAsync(
        long carId, Stream imageStream, string extension, CancellationToken cancellationToken)
    {
        diagnostics.Start("DatabaseLookup");
        var car = await repository.FindCarAsync(carId, cancellationToken);
        diagnostics.Complete(car is null ? "Missing" : "Completed");
        if (car is null)
            return (null, false);

        return await PersistAsync($"car-{carId}", imageStream, extension, async (imageName, committing) =>
        {
            await repository.SaveCarAsync(car, imageName, committing, cancellationToken);
            return (imageName, car.IsReferenceApproved);
        }, cancellationToken);
    }

    public async Task<(string? ImageName, int UpdatedCars)> SaveAsync(
        long modelId, Stream imageStream, string extension, CancellationToken cancellationToken)
    {
        diagnostics.Start("DatabaseLookup");
        var model = await repository.FindModelAsync(modelId, cancellationToken);
        diagnostics.Complete(model is null ? "Missing" : "Completed");
        if (model is null)
            return (null, 0);

        return await PersistAsync($"model-{modelId}", imageStream, extension, async (imageName, committing) =>
        {
            var updated = await repository.SaveModelAsync(model, imageName, committing, cancellationToken);
            return (imageName, updated);
        }, cancellationToken);
    }

    private async Task<TResult> PersistAsync<TResult>(string prefix, Stream input, string extension,
        Func<string, Action, Task<TResult>> update, CancellationToken cancellationToken)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, name);
        var created = false;
        var commitStarted = false;
        try
        {
            diagnostics.Start("FilePersistence");
            Directory.CreateDirectory(directory);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                created = true;
                await input.CopyToAsync(output, cancellationToken);
            }
            diagnostics.Complete();

            diagnostics.Start("DatabaseUpdate");
            var result = await update($"/images/cars/{name}", () =>
            {
                commitStarted = true;
                diagnostics.Complete();
                diagnostics.Start("DatabaseCommit");
            });
            diagnostics.Complete();
            return result;
        }
        catch
        {
            // A commit failure can be ambiguous. Retain the file if SQL might have committed.
            if (created && !commitStarted)
                TryDelete(path);
            throw;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not remove failed upload. ExceptionType={ExceptionType}",
                exception.GetType().Name);
        }
    }
}
