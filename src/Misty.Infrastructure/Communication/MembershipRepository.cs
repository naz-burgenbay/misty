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

    public async Task<IReadOnlyList<ChannelMemberDto>> ListMembersAsync(Guid channelId, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;

        var members = await (
            from m in _db.Memberships.AsNoTracking()
            where m.ChannelId == channelId && !m.IsDeleted
            join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
            where !u.IsDeleted
            select new { m.Id, m.UserId, m.JoinedAt, m.Version, u.Username, u.DisplayName, u.AvatarUrl })
            .ToListAsync(ct);

        if (members.Count == 0)
            return Array.Empty<ChannelMemberDto>();

        var membershipIds = members.Select(x => x.Id).ToList();
        var userIds = members.Select(x => x.UserId).ToList();

        var roleAssignments = await _db.MemberRoles.AsNoTracking()
            .Where(mr => membershipIds.Contains(mr.MembershipId))
            .Select(mr => new { mr.MembershipId, mr.RoleId })
            .ToListAsync(ct);

        var activeModeration = await _db.ModerationActions.AsNoTracking()
            .Where(a => a.ChannelId == channelId
                     && userIds.Contains(a.TargetUserId)
                     && a.Type != ModerationActionType.Kick)
            .Where(ModerationAction.IsActiveExpr(utcNow))
            .Select(a => new { a.TargetUserId, a.Type })
            .ToListAsync(ct);

        var rolesByMembership = roleAssignments
            .GroupBy(x => x.MembershipId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.RoleId).ToList());

        var modByUser = activeModeration
            .GroupBy(x => x.TargetUserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ModerationActionType>)g.Select(x => x.Type).Distinct().ToList());

        return members
            .OrderBy(x => x.DisplayName)
            .Select(x => new ChannelMemberDto(
                x.UserId,
                x.Username,
                x.DisplayName,
                x.AvatarUrl,
                x.JoinedAt,
                rolesByMembership.TryGetValue(x.Id, out var roles) ? roles : Array.Empty<Guid>(),
                modByUser.TryGetValue(x.UserId, out var mods) ? mods : Array.Empty<ModerationActionType>(),
                Convert.ToBase64String(x.Version)))
            .ToList();
    }
}
