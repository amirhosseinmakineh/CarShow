namespace CarShow.ApplicationService.Contract.Dtos.TipDto
{
    public record UpdateTipDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;

    }
}
