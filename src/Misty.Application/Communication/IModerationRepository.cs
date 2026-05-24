using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IModerationRepository
{
    Task<ModerationAction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasActiveAsync(Guid channelId, Guid targetUserId, ModerationActionType type, CancellationToken ct = default);
    Task<List<ModerationAction>> GetActiveForUserAsync(Guid channelId, Guid targetUserId, CancellationToken ct = default);
    Task AddAsync(ModerationAction action, CancellationToken ct = default);
    Task UpdateAsync(ModerationAction action, CancellationToken ct = default);
}
