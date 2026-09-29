namespace CarShow.ApplicationService.Contract.Dtos.TipDto
{
    public record TipDto 
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
