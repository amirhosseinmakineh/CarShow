using Microsoft.Data.SqlClient;

namespace CarShow.Controllers;

internal static class SqlServerCapacityError
{
    public static bool IsTransactionLogFull(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 9002)
            {
                return true;
            }
        }

        return false;
    }
}
