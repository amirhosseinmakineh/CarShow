namespace CarShow.Domain.Models
{
    public class Tip : BaseEntity<long> 
    {
        public string Name { get; set; } = string.Empty;
        #region Relations
        public ICollection<Car> Cars { get; set; }
        #endregion
    }
}
