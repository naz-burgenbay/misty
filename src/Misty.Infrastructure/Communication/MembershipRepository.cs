using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class MembershipRepository : IMembershipRepository
{
    private readonly ApplicationDbContext _db;

    public MembershipRepository(ApplicationDbContext db) => _db = db;

    public Task<Membership?> GetAsync(Guid channelId, Guid userId, CancellationToken ct = default)
        => _db.Memberships.FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

    public async Task AddAsync(Membership membership, Channel channel, CancellationToken ct = default)
    {
        channel.IncrementMemberCount();
        await _db.Memberships.AddAsync(membership, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Membership membership, Channel channel, CancellationToken ct = default)
    {
        channel.DecrementMemberCount();
        _db.Memberships.Remove(membership);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SoftRemoveAsync(Membership membership, Channel channel, CancellationToken ct = default)
    {
        channel.DecrementMemberCount();
        membership.SoftDelete();
        await _db.SaveChangesAsync(ct);
    }

    public Task<MemberRole?> GetRoleAssignmentAsync(Guid membershipId, Guid roleId, CancellationToken ct = default)
        => _db.MemberRoles.FirstOrDefaultAsync(mr => mr.MembershipId == membershipId && mr.RoleId == roleId, ct);

    public async Task AssignRoleAsync(MemberRole memberRole, CancellationToken ct = default)
    {
        await _db.MemberRoles.AddAsync(memberRole, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeRoleAsync(MemberRole memberRole, CancellationToken ct = default)
    {
        _db.MemberRoles.Remove(memberRole);
        await _db.SaveChangesAsync(ct);
    }
}
