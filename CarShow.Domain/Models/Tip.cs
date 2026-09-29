namespace CarShow.Domain.Models
{
    public class Tip : BaseEntity<long> 
    {
        public string TipName { get; set; } = string.Empty;
        #region Relations
        public ICollection<Car> Cars { get; set; }
        #endregion
    }
}
