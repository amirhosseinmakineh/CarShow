using CarShow.ApplicationService.Contract.Dtos.Telegram;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models.Telegram;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services;

public sealed class TelegramListingManagementService(ITelegramProcessingStore store)
{
    public async Task<TelegramListingClearResult?> ClearAsync(int expectedCount, CancellationToken cancellationToken)
    {
        TelegramListingClearResult? result = null;
        await store.RunExclusiveAsync("TelegramDailyPricing", async () =>
        {
            var listings = await store.Listings.Where(listing => !listing.IsDelete
                && listing.MatchStatus != "Ignored").ToListAsync(cancellationToken);
            if (listings.Count != expectedCount)
            {
                return;
            }

            var messageIds = listings.Where(listing => listing.RawMessageId.HasValue)
                .Select(listing => listing.RawMessageId!.Value).Distinct().ToArray();
            var preserved = 0;
            foreach (var chunk in messageIds.Chunk(1000))
            {
                var messages = await store.Messages.Where(message => chunk.Contains(message.Id))
                    .ToListAsync(cancellationToken);
                foreach (var message in messages)
                {
                    message.ProcessingStatus = "Cleared";
                    message.ProcessingError = null;
                    message.ExtractionVersion = TelegramListingExtractor.CurrentVersion;
                }
                preserved += messages.Count;
            }
            foreach (var listing in listings)
            {
                listing.IsDelete = true;
            }
            result = new TelegramListingClearResult(listings.Count, preserved);
        }, cancellationToken);
        return result;
    }
}
