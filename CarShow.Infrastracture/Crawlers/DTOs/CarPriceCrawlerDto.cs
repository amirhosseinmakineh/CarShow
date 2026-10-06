namespace CarShow.Infrastracture.Crawlers.DTOs
{
    public class CarPriceCrawlerDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public string CarName { get; set; } = string.Empty;
        public string TipName { get; set; } = string.Empty;
        public decimal MarketPrice { get; set; }
        public decimal FactoryPrice { get; set; }
        public string DetailUrl { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
