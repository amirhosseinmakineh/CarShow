namespace CarShow.ApplicationService.Contract.Dtos.CategoryDto
{
    public record UpdateCategoryDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
