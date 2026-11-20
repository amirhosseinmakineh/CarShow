using System.Data;
using System.Threading.Channels;
using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CarShow.BackgroundServices;

// Independent snapshot collector. Never invokes the price scan or its parsers.
public sealed class TelegramRawArchiveWorker : BackgroundService
{
    private static readonly string[] Channels = new[]
    {
        "autopalermo",
        "davoodkelasii",
        "tajcarsefr",
        "autozamanzade",
        "miladforoud",
        "autoland41",
        "haminkhodroo",
        "adib_khodro_qom",
        "gallery20moradi",
        "car_sales_gandomi",
        "autosolhi",
        "behzadsaberi_oxinkhodro",
        "auto_ap",
        "avatttgalry",
        "tehrankhodro97",
        "mohamadrezamansoor",
        "payamkhodrooo",
        "auto_teymouri",
        "hahabbesharaty",
        "autoesmieh",
        "omidabbasicarsale",
        "otoeiliya",
        "autogalerymhmood",
        "kharid0forosh0mashin",
        "eshraghicar",
        "irankodroohadiali",
        "namayeshgahdarantehran21",
        "hashemi_car2",
        "bazarkhodroh",
        "modern_khodrooooo",
        "thehhrankhodrow",
        "autoplack",
        "automarkethamidnasiri",
        "goroohmashin",
        "khodro_mashin_tehran_divar_bama",
        "otosefr_farzadakbari",
        "rafieicar",
        "hhirsa",
        "wolfkhodro",
        "sefidgarali",
        "aradkhodro96",
        "payamrahnama",
        "uotobehroz",
        "autoluxy",
        "otoadelkhani",
        "autogorgangolbini",
        "ehsaaant",
        "autoramin9787",
        "automeysamsaghi",
        "auto_dayy",
        "shakiba_iran_16",
        "hoseinbabaieqom",
        "auto_royal_meshkin",
        "auto_najafiqom",
        "mehrdadloghmanii",
        "alavicarexhibition",
        "mamatir_khodro_hosseini",
        "grohcobrotehran",
        "khazarrkhodro",
        "auto_1abdolmaleki1",
        "autopasdaran",
        "hakimghailarrr",
        "auto_yektaa",
        "jamshidsamadiiii",
        "auto_jahan",
        "ilam98khodro",
        "autosardarr",
        "khodro_nasri",
        "autovalayati",
        "outorashidiyan",
        "sahracar",
        "arasmehri",
        "parsiankhodroqolizade",
        "kiu2016",
        "arsinkhodro",
        "bakshabadi",
        "automojtaba66",
        "autoeightasgarinia",
        "keyvancar",
        "pejman2616",
        "auto_tabrizi",
        "hesamfatemikaraj",
        "autofezolahi",
        "irankhodromonirikashan",
    };
    private static readonly HttpClient Client = CreateClient();
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramRawArchiveWorker> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private int _pendingOrRunning;
    private volatile string _status = "Idle";
    private DateTimeOffset? _lastStartedAt;
    private DateTimeOffset? _lastFinishedAt;
    private string? _lastError;
    private volatile int _successfulChannels;
    private volatile int _failedChannels;
    private volatile int _receivedMessages;
    private volatile int _savedMessages;
    private Dictionary<string, string> _channelErrors = new();
    private readonly object _diagnosticsLock = new();
    public object Status
    {
        get
        {
            lock (_diagnosticsLock)
                return new { status = _status, lastStartedAt = _lastStartedAt,
                    lastFinishedAt = _lastFinishedAt, lastError = _lastError,
                    successfulChannels = _successfulChannels, failedChannels = _failedChannels,
                    receivedMessages = _receivedMessages, savedMessages = _savedMessages,
                    channelErrors = _channelErrors.ToDictionary(x => x.Key, x => x.Value) };
        }
    }
    public bool TryQueueManualRefresh()
    {
        if (Interlocked.CompareExchange(ref _pendingOrRunning, 1, 0) != 0) return false;
        _status = "Queued";
        if (_requests.Writer.TryWrite(true)) return true;
        Interlocked.Exchange(ref _pendingOrRunning, 0);
        _status = "Idle";
        return false;
    }
    public TelegramRawArchiveWorker(IConfiguration config, ILogger<TelegramRawArchiveWorker> logger)
    { _config = config; _logger = logger; }
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; CarShowRawArchive/1.0)");
        return client;
    }
    private SqlConnection OpenConnection() => new(_config.GetConnectionString("CarShow")
        ?? throw new InvalidOperationException("CarShow connection string missing."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Schema changes belong to EF migrations/deployment, never request or worker startup.
            // Manual-only: no initial scan and no periodic timer.
            await foreach (var _ in _requests.Reader.ReadAllAsync(stoppingToken))
            {
                _status = "Running";
                _lastStartedAt = DateTimeOffset.UtcNow;
                _lastError = null;
                _successfulChannels = 0;
                _failedChannels = 0;
                _receivedMessages = 0;
                _savedMessages = 0;
                lock (_diagnosticsLock) _channelErrors = new();
                try
                {
                    await RefreshAsync(stoppingToken);
                    _status = "Completed";
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _status = "Failed";
                    _logger.LogError(ex, "Manual Telegram raw archive refresh failed.");
                }
                finally
                {
                    _lastFinishedAt = DateTimeOffset.UtcNow;
                    Interlocked.Exchange(ref _pendingOrRunning, 0);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _status = "Failed";
            _lastError = ex.Message;
            _logger.LogError(ex, "Telegram raw archive worker could not start.");
        }
    }

    private sealed record RawPost(long MessageId, string Username, string? ChannelName,
        string? SenderName, string Text, DateTimeOffset PublishedAt)
    {
        public string Url => "https://t.me/" + Username + "/" + MessageId;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (!await _refreshLock.WaitAsync(0, ct)) return;
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var posts = new List<RawPost>();
            var successfulChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var channelErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var semaphore = new SemaphoreSlim(3);
            var tasks = Channels.Select(async username =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var result = await FetchChannelAsync(username, cutoff, ct);
                    lock (posts) { posts.AddRange(result); successfulChannels.Add(username); }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lock (posts) channelErrors[username] = ex.GetType().Name + ": " + ex.Message;
                    _logger.LogWarning(ex, "Raw Telegram fetch failed for {Channel}", username);
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);
            _successfulChannels = successfulChannels.Count;
            _failedChannels = channelErrors.Count;
            _receivedMessages = posts.Count;
            lock (_diagnosticsLock) _channelErrors = channelErrors;
            if (successfulChannels.Count == 0)
                throw new InvalidOperationException("All Telegram channels failed. See channelErrors in archive status.");
            if (posts.Count == 0)
                throw new InvalidOperationException("Telegram channels responded but no posts were found in the last 24 hours. Existing snapshot was preserved.");

            await using var connection = OpenConnection();
            await connection.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            // Stage the full successful snapshot before deleting any existing rows.
            await using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = """
                    CREATE TABLE #Incoming (
                        TelegramMessageId bigint NOT NULL, ChannelUsername nvarchar(128) NOT NULL,
                        ChannelName nvarchar(256) NULL, SenderName nvarchar(256) NULL,
                        [Text] nvarchar(max) NOT NULL, MessageUrl nvarchar(512) NOT NULL,
                        PublishedAt datetimeoffset NOT NULL, CollectedAt datetimeoffset NOT NULL
                    );
                    """;
                await create.ExecuteNonQueryAsync(ct);
            }
            var now = DateTimeOffset.UtcNow;
            var batch = new DataTable();
            batch.Columns.Add("TelegramMessageId", typeof(long));
            batch.Columns.Add("ChannelUsername", typeof(string));
            batch.Columns.Add("ChannelName", typeof(string));
            batch.Columns.Add("SenderName", typeof(string));
            batch.Columns.Add("Text", typeof(string));
            batch.Columns.Add("MessageUrl", typeof(string));
            batch.Columns.Add("PublishedAt", typeof(DateTimeOffset));
            batch.Columns.Add("CollectedAt", typeof(DateTimeOffset));
            foreach (var post in posts.DistinctBy(p => (p.Username, p.MessageId)))
                batch.Rows.Add(post.MessageId, post.Username,
                    (object?)post.ChannelName ?? DBNull.Value,
                    (object?)post.SenderName ?? DBNull.Value,
                    post.Text, post.Url, post.PublishedAt, now);
            using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#Incoming", BatchSize = 1000, BulkCopyTimeout = 120
            })
            {
                foreach (DataColumn column in batch.Columns)
                    bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                await bulk.WriteToServerAsync(batch, ct);
            }
            await using (var swap = connection.CreateCommand())
            {
                swap.Transaction = transaction;
                swap.CommandText = """
                    DELETE FROM dbo.TelegramRawMessages
                    WHERE ChannelUsername IN (SELECT ChannelUsername FROM #Incoming)
                       OR ChannelUsername IN (SELECT value FROM STRING_SPLIT(@successful, ','));
                    INSERT INTO dbo.TelegramRawMessages
                        (TelegramMessageId,ChannelUsername,ChannelName,SenderName,[Text],MessageUrl,PublishedAt,CollectedAt)
                    SELECT TelegramMessageId,ChannelUsername,ChannelName,SenderName,[Text],MessageUrl,PublishedAt,CollectedAt FROM #Incoming;
                    DELETE FROM dbo.TelegramRawMessages WHERE PublishedAt < @cutoff;
                    """;
                swap.Parameters.AddWithValue("@successful", string.Join(",", successfulChannels));
                swap.Parameters.AddWithValue("@cutoff", cutoff);
                await swap.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            _savedMessages = batch.Rows.Count;
            _logger.LogInformation("Telegram raw snapshot refreshed: {Count} messages from {Channels} channels.", posts.Count, successfulChannels.Count);
        }
        finally { _refreshLock.Release(); }
    }

    private static async Task<List<RawPost>> FetchChannelAsync(string username, DateTimeOffset cutoff, CancellationToken ct)
    {
        var result = new Dictionary<long, RawPost>();
        long? before = null;
        for (var page = 0; page < 20; page++)
        {
            var url = "https://t.me/s/" + username + (before.HasValue ? "?before=" + before.Value : "");
            using var response = await Client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            var nodes = document.DocumentNode.SelectNodes("//*[@data-post]");
            if (nodes == null || nodes.Count == 0) throw new InvalidOperationException("No public Telegram posts found.");
            long? oldest = null;
            var foundOld = false;
            foreach (var node in nodes)
            {
                var postId = node.GetAttributeValue("data-post", "");
                var split = postId.LastIndexOf('/');
                if (split <= 0 || !postId[..split].Equals(username, StringComparison.OrdinalIgnoreCase)
                    || !long.TryParse(postId[(split+1)..], out var id) || id <= 0) continue;
                if (before.HasValue && id >= before.Value) continue;
                oldest = Math.Min(oldest ?? id, id);
                var dateString = node.SelectSingleNode(".//time[@datetime]")?.GetAttributeValue("datetime", "");
                if (!DateTimeOffset.TryParse(dateString, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var published)) continue;
                if (published < cutoff) { foundOld = true; continue; }
                if (published > DateTimeOffset.UtcNow.AddMinutes(5)) continue;
                var textNode = node.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' tgme_widget_message_text ')]");
                // Clone before replacing BR elements to avoid mutating other nodes.
                var clone = textNode?.CloneNode(true);
                if (clone != null)
                    foreach (var br in clone.Descendants("br").ToList())
                        br.ParentNode?.ReplaceChild(document.CreateTextNode("\n"), br);
                var text = clone == null ? "" : HtmlEntity.DeEntitize(clone.InnerText).Trim();
                var title = node.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' tgme_widget_message_author ')]")?.InnerText.Trim();
                result[id] = new RawPost(id, username, HtmlEntity.DeEntitize(title ?? username), null, text, published);
            }
            if (foundOld || !oldest.HasValue || (before.HasValue && oldest.Value >= before.Value)) break;
            before = oldest;
            await Task.Delay(300, ct);
        }
        return result.Values.ToList();
    }
}

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/telegram/messages")]
public sealed class TelegramRawMessagesController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly TelegramRawArchiveWorker _worker;
    public TelegramRawMessagesController(IConfiguration config, TelegramRawArchiveWorker worker)
    { _config = config; _worker = worker; }
    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        if (!_worker.TryQueueManualRefresh()) return Conflict(new { message = "Refresh already queued or running." });
        return Accepted(new { status = "Queued" });
    }
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(_worker.Status);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] string? channel = null,
        [FromQuery] DateTimeOffset? fromDate = null, [FromQuery] DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var connection = new SqlConnection(_config.GetConnectionString("CarShow"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        await connection.OpenAsync(ct);
        var sql = """
            FROM dbo.TelegramRawMessages
            WHERE (@search IS NULL OR [Text] LIKE N'%' + @search + N'%' OR CONVERT(nvarchar(30),TelegramMessageId) LIKE N'%' + @search + N'%')
              AND (@channel IS NULL OR ChannelUsername LIKE N'%' + @channel + N'%' OR ChannelName LIKE N'%' + @channel + N'%')
              AND (@fromDate IS NULL OR PublishedAt >= @fromDate)
              AND (@toDate IS NULL OR PublishedAt < DATEADD(day,1,@toDate))
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = "SELECT COUNT_BIG(*) " + sql + "; SELECT Id,TelegramMessageId,ChannelId,ChannelName,ChannelUsername,SenderName,[Text],MessageUrl,PublishedAt,CollectedAt,Metadata " + sql
            + " ORDER BY PublishedAt DESC, Id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";
        cmd.Parameters.Add("@search", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim();
        cmd.Parameters.Add("@channel", SqlDbType.NVarChar, 128).Value = string.IsNullOrWhiteSpace(channel) ? DBNull.Value : channel.Trim();
        cmd.Parameters.Add("@fromDate", SqlDbType.DateTimeOffset).Value = (object?)fromDate ?? DBNull.Value;
        cmd.Parameters.Add("@toDate", SqlDbType.DateTimeOffset).Value = (object?)toDate ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@skip", (page - 1L) * pageSize);
        cmd.Parameters.AddWithValue("@take", pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt64(0);
        await reader.NextResultAsync(ct);
        var items = new List<object>();
        while (await reader.ReadAsync(ct))
            items.Add(new {
                id = Convert.ToInt64(reader.GetValue(0)), telegramMessageId = Convert.ToInt64(reader.GetValue(1)),
                channelId = reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture),
                channelName = reader.IsDBNull(3) ? null : reader.GetString(3),
                channelUsername = reader.GetString(4),
                senderName = reader.IsDBNull(5) ? null : reader.GetString(5),
                text = reader.GetString(6), messageUrl = reader.GetString(7),
                publishedAt = reader.GetDateTimeOffset(8), collectedAt = reader.GetDateTimeOffset(9),
                metadata = reader.IsDBNull(10) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(reader.GetString(10))
            });
        return Ok(new { items, totalCount = count, page, pageSize });
    }
}
