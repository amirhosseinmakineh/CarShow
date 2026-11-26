using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Data;
using Microsoft.SqlServer.TransactSql.ScriptDom;

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

        var parser = new TSql160Parser(true);
        var fragment = parser.Parse(new StringReader(request.Sql), out var errors);
        if (errors.Count != 0 || fragment is not TSqlScript script ||
            script.Batches.Count != 1 || script.Batches[0].Statements.Count != 1 ||
            script.Batches[0].Statements[0] is not SelectStatement)
            return BadRequest(new { message = "Only one SELECT statement is allowed." });
        var validator = new ArchiveQueryValidator();
        fragment.Accept(validator);
        if (!validator.Valid)
            return BadRequest(new { message = "Only read-only archive queries are allowed." });
        var generator = new Sql160ScriptGenerator();
        generator.GenerateScript(fragment, out var validatedSql);

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
            command.CommandText = validatedSql;
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
    private sealed class ArchiveQueryValidator : TSqlFragmentVisitor
    {
        public bool Valid { get; private set; } = true;
        private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "TRIM", "LTRIM", "RTRIM", "LEFT", "RIGHT", "CHARINDEX", "PATINDEX",
            "CHAR", "NCHAR", "REPLACE", "SUBSTRING", "LEN", "LOWER", "UPPER",
            "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "ABS", "ROUND",
            "CONCAT", "TRANSLATE", "ROW_NUMBER", "DENSE_RANK", "RANK", "ISNULL",
            "STRING_AGG", "REPLICATE", "REVERSE", "UNICODE", "ASCII"
        };
        public override void Visit(TSqlFragment node)
        {
            // Reject unknown table sources and potentially effectful SELECT forms.
            if (node is TableReference && node is not NamedTableReference &&
                node is not QueryDerivedTable && node is not QualifiedJoin &&
                node is not UnqualifiedJoin && node is not JoinParenthesisTableReference)
                Valid = false;
            if (node is NamedTableReference table &&
                (table.SchemaObject.Identifiers.Count != 2 ||
                 !string.Equals(table.SchemaObject.Identifiers[0].Value, "dbo", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(table.SchemaObject.Identifiers[1].Value, "TelegramRawMessages", StringComparison.OrdinalIgnoreCase) ||
                 table.TableHints.Count != 0))
                Valid = false;
            if (node is FunctionCall function &&
                (function.CallTarget != null || !Functions.Contains(function.FunctionName.Value)))
                Valid = false;
            if (node is SelectStatement select &&
                (select.Into != null || select.OptimizerHints.Count != 0 || select.WithCtesAndXmlNamespaces != null))
                Valid = false;
            if (node is SelectSetVariable || node is NextValueForExpression ||
                node is GlobalVariableExpression || node is VariableReference)
                Valid = false;
            base.Visit(node);
        }
    }
}
