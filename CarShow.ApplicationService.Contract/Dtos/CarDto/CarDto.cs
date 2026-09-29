namespace CarShow.ApplicationService.Contract.Dtos.CarDto
{
    public record CarDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string carModeName { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public float Price { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string tipName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CategoryName { get; set; }
    }
}
