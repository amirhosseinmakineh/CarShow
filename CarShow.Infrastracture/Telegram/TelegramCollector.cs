using Microsoft.Extensions.Configuration;

namespace CarShow.Infrastracture.Telegram;

public sealed class TelegramCollector(IConfiguration configuration) : ITelegramCollector
{
    public Task<TelegramCollectionResult> CollectChannelAsync(string identifier, CancellationToken cancellationToken)
    {
        var apiId = configuration["Telegram:ApiId"];
        var apiHash = configuration["Telegram:ApiHash"];
        var botToken = configuration["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(apiId) || string.IsNullOrWhiteSpace(apiHash))
        {
            var reason = string.IsNullOrWhiteSpace(botToken)
                ? "Telegram user API credentials are not configured. A Bot Token alone cannot read arbitrary public channels."
                : "Current credential is Bot Token only; Telegram Bot API cannot read arbitrary public channels without access. Configure MTProto ApiId/ApiHash and an authorized session.";
            return Task.FromResult(new TelegramCollectionResult(false, true, 0, reason));
        }

        return Task.FromResult(new TelegramCollectionResult(false, false, 0,
            "MTProto provider is not enabled in this deployment. Configure an approved Telegram MTProto adapter before collecting data."));
    }
}
