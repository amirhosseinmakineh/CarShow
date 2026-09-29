namespace CarShow.ApplicationService.Contract.Dtos.ModelDto
{
    public record UpdateModelDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
