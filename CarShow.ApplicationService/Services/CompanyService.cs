using CarShow.ApplicationService.Contract.Dtos.CompanyDto;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;

namespace CarShow.ApplicationService.Services
{
    public class CompanyService : ICompanyService
    {
        private readonly IBaseRepository<long, Company> companyRepository;

        public CompanyService(IBaseRepository<long, Company> companyRepository)
        {
            this.companyRepository = companyRepository;
        }

        public async Task<Result<object>> CreateCompany(CreateCompanyDto dto)
        {
            var company = companyRepository.GetAll()
                .FirstOrDefault(x=> x.Name == dto.Name);
            if (company is  null)
            {
                company = new Company()
                {
                    Name = dto.Name,
                };
                await companyRepository.Create(company);
                await companyRepository.SaveChanges();
                return Result<object>.Success(company, "شرکت با موفقیت ثبت شد");
            }
            else
                return Result<object>.Failure("نام شرکت در سیستم موجود میباشد");
        }

        public async Task<Result<string>> DeleteCompany(long id)
        {
            var company = await companyRepository.GetById(id);
            if (company is not null)
            {
                await companyRepository.Delete(company);
                await companyRepository.SaveChanges();
                return Result<string>.Success("شرکت با موفقیت حذف شد");
            }
            else
                return Result<string>.Failure("شرکتی یافت نشد");
        }

        public Result<List<CompanyDto>> GetCompanies()
        {
            var result =  companyRepository.GetAll().Select(x => new CompanyDto()
            {
                Id = x.Id,
                Name = x.Name,
            }).ToList();
            return Result<List<CompanyDto>>.Success(result);
        }

        public async Task<Result<object>> UpdateCompany(UpdateCompanyDto dto)
        {
            var company = await companyRepository.GetById(dto.Id);
            if (company is not null) 
            { 
            company.Id = dto.Id;
            company.Name = dto.Name;
            await companyRepository.Update(company);
            await companyRepository.SaveChanges();
            return Result<object>.Success(company);
            }else
                return Result<object>.Failure("شرکتی یافت نشد");
        }
    }
}
