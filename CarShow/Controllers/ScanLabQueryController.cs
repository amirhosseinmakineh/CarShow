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
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { message = "SQL must not be empty." });

        var connectionString = configuration.GetConnectionString("CarShow");
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

            await using var command = connection.CreateCommand();
            command.CommandText = request.Sql;
            command.CommandTimeout = 10;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, token);
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<string[]>();
            const int limit = 10000;
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
