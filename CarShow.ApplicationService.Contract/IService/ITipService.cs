using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.TipDto;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface ITipService
    {
        Result<List<TipDto>> GetTips();
        Task<Result<object>> CreateTip(CreateTipDto dto);
        Task<Result<object>> UpdateTip(UpdateTipDto dto);
        Task<Result<string>> DeleteTip(long id);
    }
}
