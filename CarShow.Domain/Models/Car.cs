namespace CarShow.Domain.Models
{
    public class Car : BaseEntity<long>
    {
        public bool IsUsed { get; set; }
        // Telegram-derived catalog identity is scoped by year to prevent price histories
        // for different model years from being combined. Null is retained for legacy cars.
        public int? Year { get; set; }
        public string? YearCalendar { get; set; }
        public string? TelegramIdentityKey { get; set; }
        public long CompanyId { get; set; }
        public long CarModelId { get; set; }
        public long? TipId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public DateOnly? MarketPriceDay { get; set; }
        public int PriceSampleCount { get; set; }
        public DateTime? PriceCalculatedAt { get; set; }
        public decimal MarketPrice { get; set; }
        public decimal FactoryPrice { get; set; }

        public string SourceUrl { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }

        public Company Company { get; set; } = null!;
        public CarModel CarModel { get; set; } = null!;
        public Tip? Tip { get; set; }
    }
}
