namespace CarShow.Domain.Models
{
    public class Company : BaseEntity<long>
    {
        public string Name { get; set; } = string.Empty;

        public ICollection<Car> Cars { get; set; } = new List<Car>();
    }
}
