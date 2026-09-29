namespace CarShow.ApplicationService.Contract.IService
{
    public interface IInstallmentCalculator
    {
        decimal CalculateInstallment(decimal carPrice, decimal prePayment, int time);
    }
}