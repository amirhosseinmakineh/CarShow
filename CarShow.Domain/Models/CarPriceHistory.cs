namespace CarShow.Domain.Models
{
    public class CarPriceHistory : BaseEntity<long>
    {
        public DateOnly? PriceDay { get; set; }
        public int SampleCount { get; set; }
        public DateTime? CalculatedAt { get; set; }
        public bool IsFinal { get; set; }
        public long CarId { get; set; }

        public decimal MarketPrice { get; set; }

        public decimal FactoryPrice { get; set; }

        public DateTime Date { get; set; }

        public Car Car { get; set; } = null!;
    }
}
