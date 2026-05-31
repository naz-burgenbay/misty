using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> ListForUserAsync(Guid userId, CancellationToken ct = default);
    Task CreateWithOwnerAsync(Channel channel, ChannelRole ownerRole, Membership creatorMembership, MemberRole ownerMemberRole, CancellationToken ct = default);
    Task UpdateAsync(Channel channel, byte[] concurrencyToken, CancellationToken ct = default);
    Task UpdateIconUrlAsync(Channel channel, string? iconUrl, CancellationToken ct = default);
    Task SoftDeleteAsync(Channel channel, CancellationToken ct = default);
}
