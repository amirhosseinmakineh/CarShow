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
        if (string.IsNullOrWhiteSpace(connectionString))
            return StatusCode(503, new { message = "ScanLabReadOnly connection is not configured. No query was executed." });

        if (!await Gate.WaitAsync(0, cancellationToken))
            return StatusCode(429, new { message = "Another query is running." });

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var token = deadline.Token;
            var watch = Stopwatch.StartNew();
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(token);

            // Fail closed: never use the application's privileged connection.
            await using var check = connection.CreateCommand();
            check.CommandText = """
                SELECT CASE WHEN USER_NAME() = N'ScanLabReader'
                  AND IS_SRVROLEMEMBER('sysadmin') = 0
                  AND IS_ROLEMEMBER('db_owner') = 0
                  AND IS_ROLEMEMBER('db_datawriter') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 0
                  AND HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SELECT') = 0
                  AND HAS_PERMS_BY_NAME('dbo.TelegramRawMessages', 'OBJECT', 'SELECT') = 1
                  THEN 1 ELSE 0 END
                """;
            check.CommandTimeout = 5;
            if (Convert.ToInt32(await check.ExecuteScalarAsync(token)) != 1)
                return StatusCode(503, new { message = "Dedicated restricted ScanLabReader principal required." });

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
