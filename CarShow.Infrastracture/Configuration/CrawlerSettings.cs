namespace CarShow.Infrastracture.Configuration
{
    public record CrawlerSettings
    {
        public const string SectionName = "CrawlerSettings";

        public string BaseUrl { get; set; } = string.Empty;
        public string PricesPath { get; set; } = string.Empty;
        public string DetailPath { get; set; } = string.Empty;
    }
}
