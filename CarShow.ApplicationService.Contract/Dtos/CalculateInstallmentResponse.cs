public class CalculateInstallmentResponse
{
    public decimal InstallmentAmount { get; set; }
    public decimal MonthlyInterestRate { get; set; }
    public decimal TotalInterest { get; set; }
    public decimal TotalPayment { get; set; }
    public decimal RemainingBalance { get; set; }
}