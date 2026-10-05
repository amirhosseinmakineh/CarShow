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
            var tip = tipRepository.GetAll().FirstOrDefault(x => x.Name == dto.Name);
            if (tip is null)
            {
                tip = new Tip { Name = dto.Name };
                await tipRepository.Create(tip);
                await tipRepository.SaveChanges();
                return Result<object>.Success(tip, "تیپ مورد نظر با موفقیت ثبت شد");
            }

            return Result<object>.Failure("تیپ مورد نظر در سیستم موجود می‌باشد");
        }

        public async Task<Result<string>> DeleteTip(long id)
        {
            var tip = await tipRepository.GetById(id);
            if (tip is null)
                return Result<string>.Failure("تیپی یافت نشد");

            await tipRepository.Delete(tip);
            await tipRepository.SaveChanges();
            return Result<string>.Success("تیپ مورد نظر با موفقیت حذف شد");
        }

        public Result<List<TipDto>> GetTips()
        {
            var result = tipRepository.GetAll()
                .Select(x => new TipDto { Id = x.Id, Name = x.Name })
                .ToList();

            return Result<List<TipDto>>.Success(result);
        }

        public async Task<Result<object>> UpdateTip(UpdateTipDto dto)
        {
            var tip = await tipRepository.GetById(dto.Id);
            if (tip is null)
                return Result<object>.Failure("تیپ یافت نشد");

            tip.Name = dto.Name;
            await tipRepository.Update(tip);
            await tipRepository.SaveChanges();
            return Result<object>.Success(tip, "ویرایش تیپ با موفقیت انجام شد");
        }
    }
}
