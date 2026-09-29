using CarShow.ApplicationService.Contract.Dtos.CompanyDto;

namespace CarShow.ApplicationService.Contract.IService
{
    public interface ICompanyService
    {
        Result<List<CompanyDto>> GetCompanies();
        Task<Result<object>> CreateCompany(CreateCompanyDto dto);
        Task<Result<object>> UpdateCompany(UpdateCompanyDto dto);
        Task<Result<string>> DeleteCompany(long id);
    }


}
