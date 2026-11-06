namespace CarShow.Domain.Crawling;

public sealed class CarSourceData
{
    public string CompanyName { get; set; } = string.Empty;
    public string CarName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? TipName { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal FactoryPrice { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public CarSourceDetails? Details { get; set; }
}

public sealed class CarSourceDetails
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public List<CarDetailSection> Sections { get; set; } = new();
}

public sealed class CarDetailSection
{
    public string Name { get; set; } = string.Empty;
    public List<CarSpecification> Specifications { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();
}

public sealed class CarSpecification
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
