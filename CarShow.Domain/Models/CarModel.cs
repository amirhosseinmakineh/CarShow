namespace CarShow.Domain.Models
{
    public class CarModel : BaseEntity<long>
    {
        public string Name { get; set; } = string.Empty;
        public string? ImageName { get; set; }

        public ICollection<Car> Cars { get; set; } = new List<Car>();
    }
}
