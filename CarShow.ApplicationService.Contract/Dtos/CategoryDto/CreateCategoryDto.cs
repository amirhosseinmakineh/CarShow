namespace CarShow.ApplicationService.Contract.Dtos.CategoryDto
{
    public record CreateCategoryDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
