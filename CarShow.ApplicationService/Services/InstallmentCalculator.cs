using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using System;

namespace CarShow.ApplicationService.Services
{
    public class InstallmentCalculator : IInstallmentCalculator
    {

        public decimal CalculateInstallment(decimal carPrice, decimal prePayment, int time)
        {
            if (carPrice <= 0)
                throw new ArgumentException("قیمت خودرو باید بزرگتر از صفر باشد");

            if (prePayment < 0 || prePayment >= carPrice)
                throw new ArgumentException("پیش‌پرداخت باید بین صفر و قیمت خودرو باشد");

            if (time <= 0)
                throw new ArgumentException("تعداد اقساط باید بزرگتر از صفر باشد");
            decimal balance = carPrice - prePayment;

            decimal interest = balance * 7 * (time / 2m + 0.5m);
            interest = interest / 100;

            decimal installmentAmount = (interest + balance) / time;

            return installmentAmount;
        }
    }
}