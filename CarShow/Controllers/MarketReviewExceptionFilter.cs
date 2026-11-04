using CarShow.Domain.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Controllers;

public sealed class MarketReviewExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        if (exception is ArgumentException argument)
            context.Result = new BadRequestObjectResult(new { message = argument.Message });
        else if (exception is TelegramProcessingBusyException || exception is SqlException { Number: 51000 })
            context.Result = new ConflictObjectResult(new { message = "پردازش دیگری فعال است؛ چند لحظه دیگر دوباره تلاش کنید." });
        else if (exception is DbUpdateException { InnerException: SqlException sql } && sql.Number is 2601 or 2627)
            context.Result = new ConflictObjectResult(new { message = "این منبع یا مقدار قبلاً ثبت شده است. فهرست را دوباره بارگذاری کنید." });
        else if (SqlServerCapacityError.IsTransactionLogFull(exception))
            context.Result = new ObjectResult(new { message = "فضای لاگ دیتابیس کافی نیست؛ تغییر ذخیره نشد. شناسه درخواست را به مدیر سرویس بدهید." }) { StatusCode = 507 };
        else return;
        context.ExceptionHandled = true;
    }
}
