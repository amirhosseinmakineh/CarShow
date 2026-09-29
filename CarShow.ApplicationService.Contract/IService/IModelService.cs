using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.ModelDto;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface IModelService 
    {
        Result<List<ModelDto>> GetModels();
        Task<Result<object>> CreateModel(CreateModelDto dto);
        Task<Result<object>> UpdateModel(UpdateModelDto dto);
        Task<Result<string>> DeleteModel(long id);
    }


}
