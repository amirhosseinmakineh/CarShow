using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.Dtos.ModelDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;

namespace CarShow.ApplicationService.Services
{
    public class ModelService : IModelService
    {
        private readonly IBaseRepository<long, Model> modelRepository;

        public ModelService(IBaseRepository<long, Model> modelRepository)
        {
            this.modelRepository = modelRepository;
        }

        public async Task<Result<object>> CreateModel(CreateModelDto dto)
        {
            var model = modelRepository.GetAll().FirstOrDefault(x => x.Name == dto.Name);
            if (model is  null)
            {
                model = new Model()
                {
                    Name = dto.Name,
                };
                await modelRepository.Create(model);
                await modelRepository.SaveChanges();
                return Result<object>.Success(model,"مدل مورد نظر با موفقیت ثبت شد ");
            }
            else
                return Result<object>.Failure("مدل از قبل در سیسام موجود میباشد");
        }

        public async Task<Result<string>> DeleteModel(long id)
        {
            var model = await modelRepository.GetById(id);
            if (model is not null)
            {
                await modelRepository.Delete(model);
                await modelRepository.SaveChanges();
                return Result<string>.Success("حذف مذل با موفقیت اتنجام شد");
            }
            else
                return Result<string>.Failure("مدلی یافت نشد");
        }

        public Result<List<ModelDto>> GetModels()
        {
            var result =  modelRepository.GetAll().Select(x=> new ModelDto()
            {
                Id  = x.Id,
                Name = x.Name,
            }).ToList();
            return Result<List<ModelDto>>.Success(result);
        }

        public async Task<Result<object>> UpdateModel(UpdateModelDto dto)
        {
            var model = await modelRepository.GetById(dto.Id);
            if (model is not null)
            {
                model.Name = dto.Name;
                model.Id = dto.Id;
                await modelRepository.Update(model);
                await modelRepository.SaveChanges();
                return Result<object>.Success(model, "مدل با موفقیت ویرایش شد");
            }
            else
                return Result<object>.Failure("مدلی یافت نشد");
        }
    }
}
