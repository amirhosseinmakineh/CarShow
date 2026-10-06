namespace CarShow.ApplicationService.Contract.Dtos.CarDto;

public sealed class CarDetailsDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public List<CarDetailSectionDto> Sections { get; set; } = new();
}

public sealed class CarDetailSectionDto
{
    public string Name { get; set; } = string.Empty;
    public List<CarSpecificationDto> Specifications { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();
}

public sealed class CarSpecificationDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
