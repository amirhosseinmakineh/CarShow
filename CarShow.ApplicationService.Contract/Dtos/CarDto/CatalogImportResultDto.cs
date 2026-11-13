namespace CarShow.ApplicationService.Contract.Dtos.CarDto;

public sealed record CatalogImportResultDto(
    int TotalRows,
    int ImportedCars,
    int ExistingCars,
    int RejectedRows,
    int CreatedModels,
    int CreatedCompanies,
    int CreatedTips);
