namespace CarShow.Domain.Models
{
    public class Model : BaseEntity<long>
    {
        public string Name { get; set; } = string.Empty;
        #region Relaions
        public ICollection<Car> Cars { get; set; }
        #endregion
    }
}
