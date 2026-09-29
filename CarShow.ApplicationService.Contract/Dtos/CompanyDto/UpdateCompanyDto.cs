namespace CarShow.ApplicationService.Contract.Dtos.CompanyDto
{
    public record UpdateCompanyDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
