using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.TipDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;

namespace CarShow.ApplicationService.Services
{
    public class TipService : ITipService
    {
        private readonly IBaseRepository<long, Tip> tipRepository;
public TipService(IBaseRepository<long, Tip> tipRepository)
        {
            this.tipRepository = tipRepository;
        }

        public async Task<Result<object>> CreateTip(CreateTipDto dto)
        {
            var tip = tipRepository.GetAll().FirstOrDefault(x=> x.TipName == dto.Name);
            if (tip is  null)
            {
                tip = new Tip()
                {
                    TipName = dto.Name
                };
                await tipRepository.Create(tip);
                await tipRepository.SaveChanges();
                return Result<object>.Success(tip, "تیپ مورد نظر با موفقیت ثبت شد");
            }
            else
                return Result<object>.Failure("تیبپ مورد نظر در سیستم موجود میباشد");
        }

        public async Task<Result<string>> DeleteTip(long id)
        {
            var tip = await tipRepository.GetById(id);
            if (tip is not null)
            {
                await tipRepository.Delete(tip);
                await tipRepository.SaveChanges();
                return Result<string>.Success("تیپ مورد نظر با موفقیت حذف شد");
            }
            else
                return Result<string>.Failure("تیپی یافت نشد");
        }

        public Result<List<TipDto>> GetTips()
        {
            var result =  tipRepository.GetAll().Select(x => new TipDto()
            {
                Id = x.Id,
                Name = x.TipName
            }).ToList();
            return Result<List<TipDto>>.Success(result);
        }

        public async Task<Result<object>> UpdateTip(UpdateTipDto dto)
        {
            var tip = await tipRepository.GetById(dto.Id);
            if (tip is not null)
            {
                tip.Id = dto.Id;
                tip.TipName = dto.Name;
                await tipRepository.Update(tip);
                await tipRepository.SaveChanges();
                return Result<object>.Success(tip, "ویرایش تیپ با موفقیت انجام شد");
            }
            else
                return Result<object>.Failure("تیپ یافت نشد");
        }
    }
}
