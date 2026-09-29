namespace CarShow.Domain.Models
{
    public class Company : BaseEntity<long>
    {
        public string Name { get; set; } = string.Empty;
        #region Relation
        public ICollection<Car> Cars { get; set; }
        #endregion
    }
}
