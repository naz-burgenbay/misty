using Misty.Domain.Communication;

namespace Misty.Application.Communication.Contracts;

public sealed record ChannelSummary(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions);

public interface IChannelQueryService
{
    Task<ChannelSummary?> GetByIdAsync(Guid channelId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid channelId, CancellationToken ct = default);
}
