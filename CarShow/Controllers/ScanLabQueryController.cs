using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Data;

namespace CarShow.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/telegram/scan-lab/query")]
public sealed class ScanLabQueryController(IConfiguration configuration) : ControllerBase
{
    public sealed record QueryRequest(string Sql);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    [HttpPost]
    public async Task<IActionResult> Execute(QueryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql) || request.Sql.Length > 16000)
            return BadRequest(new { message = "SQL must contain 1–16000 characters." });

        var connectionString = configuration.GetConnectionString("ScanLabReadOnly");
        var useLocalPrincipal = string.IsNullOrWhiteSpace(connectionString);
        if (useLocalPrincipal)
            connectionString = configuration.GetConnectionString("CarShow");
        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(503, new { message = "Database connection is not configured. No query was executed." });

        if (!await Gate.WaitAsync(0, cancellationToken))
            return StatusCode(429, new { message = "Another query is running." });

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var token = deadline.Token;
            var watch = Stopwatch.StartNew();
            var builder = new SqlConnectionStringBuilder(connectionString) { Pooling = false };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token);

            // Provision a database-only principal, then irreversibly drop privileges
            // for this physical session before accepting any user SQL.
            if (useLocalPrincipal)
            {
                await using var setup = connection.CreateCommand();
                setup.CommandTimeout = 5;
                setup.CommandText = """
                    IF DATABASE_PRINCIPAL_ID(N'ScanLabSessionReader') IS NULL
                    BEGIN
                        CREATE USER [ScanLabSessionReader] WITHOUT LOGIN;
                        GRANT SELECT ON OBJECT::dbo.TelegramRawMessages TO [ScanLabSessionReader];
                        DENY INSERT, UPDATE, DELETE, EXECUTE TO [ScanLabSessionReader];
                    END;
                    EXECUTE AS USER = 'ScanLabSessionReader' WITH NO REVERT;
                    """;
                await setup.ExecuteNonQueryAsync(token);
            }

            // Fail closed if the effective database principal is not restricted.
            await using var check = connection.CreateCommand();
            check.CommandText = """
                SELECT CASE WHEN USER_NAME() IN (N'ScanLabReader', N'ScanLabSessionReader')
                  AND (USER_NAME() = N'ScanLabSessionReader' OR IS_SRVROLEMEMBER('sysadmin') = 0)
                  AND IS_ROLEMEMBER('db_owner') = 0
                  AND IS_ROLEMEMBER('db_datawriter') = 0
                  AND IS_ROLEMEMBER('db_securityadmin') = 0
                  AND IS_ROLEMEMBER('db_ddladmin') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SELECT') = 0
                  AND HAS_PERMS_BY_NAME('dbo.TelegramRawMessages', 'OBJECT', 'SELECT') = 1
                  THEN 1 ELSE 0 END
                """;
            check.CommandTimeout = 5;
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) != 1)
                return StatusCode(503, new { message = "Restricted scan lab database principal required." });

            await using var command = connection.CreateCommand();
            command.CommandText = request.Sql;
            command.CommandTimeout = 10;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, token);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<string[]>();
            const int limit = 500;
            var truncated = false;
            while (await reader.ReadAsync(token))
            {
                if (rows.Count == limit) { truncated = true; break; }
                var row = new string[columns.Length];
                for (var i = 0; i < row.Length; i++)
                {
                    var value = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
                    row[i] = value is { Length: > 4000 } ? value[..4000] : value;
                }
                rows.Add(row);
            }
            return Ok(new { columns, rows, rowCount = rows.Count, truncated, elapsedMs = watch.ElapsedMilliseconds });
        }
        catch (SqlException ex) { return BadRequest(new { message = ex.Message, number = ex.Number }); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return StatusCode(408, new { message = "Query time limit exceeded." }); }
        finally { Gate.Release(); }
    }
}
