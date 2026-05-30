using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IMembershipRepository
{
    Task<Membership?> GetAsync(Guid channelId, Guid userId, CancellationToken ct = default);
    Task AddAsync(Membership membership, Channel channel, CancellationToken ct = default);
    Task RemoveAsync(Membership membership, Channel channel, CancellationToken ct = default);
    Task SoftRemoveAsync(Membership membership, Channel channel, CancellationToken ct = default);
    Task<MemberRole?> GetRoleAssignmentAsync(Guid membershipId, Guid roleId, CancellationToken ct = default);
    Task AssignRoleAsync(MemberRole memberRole, CancellationToken ct = default);
    Task RevokeRoleAsync(MemberRole memberRole, CancellationToken ct = default);
}
