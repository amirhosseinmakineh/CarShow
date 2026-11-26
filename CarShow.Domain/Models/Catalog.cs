namespace CarShow.Domain.Models;

public class Catalog : BaseEntity<long>
{
    public string CompanyName { get; set; } = string.Empty;
    public string CarName { get; set; } = string.Empty;
    public string TrimName { get; set; } = string.Empty;
}
