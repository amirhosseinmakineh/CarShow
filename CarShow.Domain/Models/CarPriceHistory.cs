namespace CarShow.Domain.Models
{
    public class CarPriceHistory : BaseEntity<long>
    {
        public long CarId { get; set; }

        public decimal MarketPrice { get; set; }

        public decimal FactoryPrice { get; set; }

        public DateTime Date { get; set; }

        public Car Car { get; set; } = null!;
    }
}
