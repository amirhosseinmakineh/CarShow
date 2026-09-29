namespace CarShow.ApplicationService.Contract.Dtos.CompanyDto
{
    public record CreateCompanyDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
