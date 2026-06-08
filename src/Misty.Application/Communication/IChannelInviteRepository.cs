using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IChannelInviteRepository
{
    Task<ChannelInvite?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ChannelInvite?> GetPendingAsync(Guid channelId, Guid invitedUserId, CancellationToken ct = default);
    Task AddAsync(ChannelInvite invite, CancellationToken ct = default);
    Task UpdateAsync(ChannelInvite invite, byte[] concurrencyToken, CancellationToken ct = default);
}
