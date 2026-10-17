using CarShow.ApplicationService.Contract.Dtos.Telegram;

namespace CarShow.ApplicationService.Contract.IService;

public interface ITelegramChannelService
{
    Task<IReadOnlyList<TelegramChannelDto>> GetChannelsAsync(CancellationToken cancellationToken);
    Task<TelegramChannelSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);
    Task<BulkTelegramChannelImportResult> ImportAsync(BulkTelegramChannelImportRequest request, CancellationToken cancellationToken);
    Task<TelegramChannelDto?> AddAsync(AddTelegramChannelRequest request, CancellationToken cancellationToken);
    Task<bool> SetEnabledAsync(long id, bool enabled, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken);
    Task<bool> RetryAsync(long id, CancellationToken cancellationToken);
    Task<bool> TestAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TelegramJobLogDto>> GetJobsAsync(CancellationToken cancellationToken);
    Task<TelegramCapabilityDto> GetCapabilitiesAsync(CancellationToken cancellationToken);
}
