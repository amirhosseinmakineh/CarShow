namespace CarShow.ApplicationService.Contract.Dtos.CarDto
{
    public record UpdateCarDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Create { get; set; } = string.Empty;
        public float Price { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Tip { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public bool IsDelete { get; set; }
    }
}
