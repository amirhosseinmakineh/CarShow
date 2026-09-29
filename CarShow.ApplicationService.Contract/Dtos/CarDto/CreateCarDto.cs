using Microsoft.AspNetCore.Http;

namespace CarShow.ApplicationService.Contract.Dtos.CarDto
{
    public record CreateCarDto
    {
        public string Name { get; set; } = string.Empty;
        public long CarModelId { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public float Price { get; set; }
        public IFormFile ImageName { get; set; }
        public string Description { get; set; } = string.Empty;
        public long TipId { get; set; }
        public long CompanyId { get; set; }
        public long CategoryId { get; set; }
    }
}
