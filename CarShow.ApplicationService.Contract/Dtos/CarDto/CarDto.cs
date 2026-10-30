namespace CarShow.ApplicationService.Contract.Dtos.CarDto
{
    public record CarDto
    {
        public long Id { get; set; }
        public bool? IsUsed { get; set; }
        public int? Year { get; set; }
        public string? YearCalendar { get; set; }
        public int? Mileage { get; set; }
        public decimal? ComparisonAveragePrice { get; set; }
        public string? ComparisonStatus { get; set; }
        public string Name { get; set; } = string.Empty;
        public string carModeName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public float Price { get; set; }
        public DateOnly? MarketPriceDay { get; set; }
        public int PriceSampleCount { get; set; }
        public DateTime? PriceCalculatedAt { get; set; }
        public decimal MarketPrice { get; set; }
        public decimal FactoryPrice { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public string tipName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public CarDetailsDto? Details { get; set; }
    }
}
