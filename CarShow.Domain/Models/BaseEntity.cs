namespace CarShow.Domain.Models
{
    public class BaseEntity<Tkey> where Tkey : struct
    {
        public Tkey Id { get; set; }
        public bool IsDelete { get; set; }
    }
}
