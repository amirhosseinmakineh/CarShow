namespace CarShow.Domain.Models
{
    public class Car : BaseEntity<long> 
    {
        public long CategoryId { get; set; }
        public long TipId { get; set; }
        public long CarModelId { get; set; }
        public long CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public float  Price { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        #region Relations
        public Category Category { get; set; }
        public Tip Tip { get; set; }
        public Model CarModel { get; set; }
        public Company Company { get; set; }
        #endregion
    }
}
