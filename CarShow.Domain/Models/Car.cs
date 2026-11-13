using System.ComponentModel.DataAnnotations.Schema;

namespace CarShow.Domain.Models
{
    public class Car : BaseEntity<long>
    {
        public long CategoryId { get; set; }
        public long TipId { get; set; }
        public long CarModelId { get; set; }
        public long CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal FactoryPrice { get; set; } = 0m;
        public decimal? MarketPrice { get; set; }
        public DateTime? LastUpdated { get; set; }
        public string Slug { get; set; } = string.Empty;
        public bool IsUsed { get; set; }
        public DateTime? MarketPriceDay { get; set; }
        public DateTime? PriceCalculatedAt { get; set; }
        public int PriceSampleCount { get; set; } = 0;
        public int? Year { get; set; }
        public string YearCalendar { get; set; } = string.Empty;
        public string? TelegramIdentityKey { get; set; }

        // Legacy DTO compatibility: these properties are not SQL columns.
        [NotMapped]
        public float Price
        {
            get => (float)(MarketPrice ?? 0m);
            set => MarketPrice = (decimal)value;
        }

        [NotMapped]
        public string StartDate
        {
            get => YearCalendar ?? string.Empty;
            set => YearCalendar = value;
        }

        public Category Category { get; set; } = null!;
        public Tip Tip { get; set; } = null!;
        public Model CarModel { get; set; } = null!;
        public Company Company { get; set; } = null!;
    }
}
