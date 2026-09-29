namespace CarShow.Domain.Models
{
    public class Category : BaseEntity<long>
    {
        public string Name { get; set; } = string.Empty;
        public int? ParentId  { get; set; }
        #region Relatons
        public ICollection<Car> Car { get; set; }
        #endregion
    }
}
