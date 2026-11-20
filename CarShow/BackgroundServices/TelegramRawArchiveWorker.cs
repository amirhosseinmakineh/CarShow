using System.Data;
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

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF OBJECT_ID(N'dbo.TelegramRawMessages', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TelegramRawMessages (
                    Id bigint IDENTITY(1,1) PRIMARY KEY,
                    TelegramMessageId bigint NOT NULL,
                    ChannelId nvarchar(128) NULL,
                    ChannelName nvarchar(256) NULL,
                    ChannelUsername nvarchar(128) NOT NULL,
                    SenderName nvarchar(256) NULL,
                    [Text] nvarchar(max) NOT NULL,
                    MessageUrl nvarchar(512) NOT NULL,
                    PublishedAt datetimeoffset NOT NULL,
                    CollectedAt datetimeoffset NOT NULL,
                    Metadata nvarchar(max) NULL,
                    CONSTRAINT UQ_TelegramRawMessages_ChannelMessage UNIQUE(ChannelUsername, TelegramMessageId)
                );
                CREATE INDEX IX_TelegramRawMessages_PublishedAt ON dbo.TelegramRawMessages(PublishedAt DESC);
            END;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureSchemaAsync(stoppingToken);
            await RefreshAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(10));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Telegram raw archive worker stopped unexpectedly."); }
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
                catch (Exception ex) { _logger.LogWarning(ex, "Raw Telegram fetch failed for {Channel}", username); }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);
            if (successfulChannels.Count == 0)
            {
                _logger.LogWarning("No Telegram channels succeeded; previous snapshot retained.");
                return;
            }

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
            foreach (var post in posts.DistinctBy(p => (p.Username, p.MessageId)))
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO #Incoming(TelegramMessageId,ChannelUsername,ChannelName,SenderName,[Text],MessageUrl,PublishedAt,CollectedAt)
                    VALUES(@id,@channel,@name,@sender,@text,@url,@published,@collected);
                    """;
                insert.Parameters.AddWithValue("@id", post.MessageId);
                insert.Parameters.AddWithValue("@channel", post.Username);
                insert.Parameters.AddWithValue("@name", (object?)post.ChannelName ?? DBNull.Value);
                insert.Parameters.AddWithValue("@sender", (object?)post.SenderName ?? DBNull.Value);
                insert.Parameters.AddWithValue("@text", post.Text);
                insert.Parameters.AddWithValue("@url", post.Url);
                insert.Parameters.AddWithValue("@published", post.PublishedAt);
                insert.Parameters.AddWithValue("@collected", now);
                await insert.ExecuteNonQueryAsync(ct);
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
    public TelegramRawMessagesController(IConfiguration config) { _config = config; }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null, [FromQuery] string? channel = null,
        [FromQuery] DateTimeOffset? fromDate = null, [FromQuery] DateTimeOffset? toDate = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        await using var connection = new SqlConnection(_config.GetConnectionString("CarShow"));
        await connection.OpenAsync(cancellationToken);
        var sql = """
            FROM dbo.TelegramRawMessages
            WHERE (@search IS NULL OR [Text] LIKE N'%' + @search + N'%' OR CONVERT(nvarchar(30),TelegramMessageId) LIKE N'%' + @search + N'%')
              AND (@channel IS NULL OR ChannelUsername LIKE N'%' + @channel + N'%' OR ChannelName LIKE N'%' + @channel + N'%')
              AND (@fromDate IS NULL OR PublishedAt >= @fromDate)
              AND (@toDate IS NULL OR PublishedAt < DATEADD(day,1,@toDate))
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT_BIG(*) " + sql + "; SELECT Id,TelegramMessageId,ChannelId,ChannelName,ChannelUsername,SenderName,[Text],MessageUrl,PublishedAt,CollectedAt,Metadata " + sql
            + " ORDER BY PublishedAt DESC, Id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;";
        cmd.Parameters.AddWithValue("@search", (object?)search?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@channel", (object?)channel?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromDate", (object?)fromDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@toDate", (object?)toDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@skip", (page - 1L) * pageSize);
        cmd.Parameters.AddWithValue("@take", pageSize);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt64(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new {
                id = reader.GetInt64(0), telegramMessageId = reader.GetInt64(1),
                channelId = reader.IsDBNull(2) ? null : reader.GetString(2),
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
