namespace CarShow.ApplicationService.Contract.Dtos.CarDto
{
    public record GetCarForCreateDto
    {
        public List<CarShow.ApplicationService.Contract.Dtos.TipDto.TipDto> Tips { get; set; }
        public List<ModelDto.ModelDto> Models { get; set; }
        public List<CompanyDto.CompanyDto> Companys { get; set; }
        public List<CategoryDto.CategoryDto> Categories { get; set; }

    }
}
