using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IChannelRoleRepository
{
    Task<ChannelRole?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ChannelRole>> GetByChannelIdAsync(Guid channelId, CancellationToken ct = default);
    Task AddAsync(ChannelRole role, CancellationToken ct = default);
    Task UpdateAsync(ChannelRole role, CancellationToken ct = default);
    Task DeleteAsync(ChannelRole role, CancellationToken ct = default);
}
