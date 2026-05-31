using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

// Resolves permissions directly from SQL. Used by CachedPermissionService when a cache entry is missing.
public sealed class PermissionService : IPermissionService
{
    internal const long DeniedSentinel = long.MinValue; // banned or non-member

    private static readonly ChannelPermission WriteMask =
        ChannelPermission.SendMessages
        | ChannelPermission.AttachFiles
        | ChannelPermission.AddReactions
        | ChannelPermission.MentionEveryone;

    private readonly ApplicationDbContext _db;

    public PermissionService(ApplicationDbContext db) => _db = db;

    public async Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default)
    {
        var effective = await ComputeEffectivePermissionsAsync(userId, channelId, ct);
        if (effective == DeniedSentinel) return false;
        return ((ChannelPermission)effective & permission) == permission;
    }

    public async Task<ChannelPermission> GetEffectivePermissionsAsync(
        Guid userId,
        Guid channelId,
        CancellationToken ct = default)
    {
        var effective = await ComputeEffectivePermissionsAsync(userId, channelId, ct);
        return effective == DeniedSentinel ? ChannelPermission.None : (ChannelPermission)effective;
    }

// Computes effective channel permissions from SQL. Muted users lose write permissions; banned users and non-members receive DeniedSentinel.
    internal async Task<long> ComputeEffectivePermissionsAsync(
        Guid userId,
        Guid channelId,
        CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;

        // If banned, deny everything
        var isBanned = await _db.ModerationActions
            .Where(m => m.ChannelId == channelId
                     && m.TargetUserId == userId
                     && m.Type == ModerationActionType.Ban)
            .AnyAsync(ModerationAction.IsActiveExpr(utcNow), ct);

        if (isBanned)
            return DeniedSentinel;

        // If not a member, deny everything
        var membership = await _db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        if (membership is null)
            return DeniedSentinel;

        // Aggregate flags from all assigned roles
        var rolePerms = await _db.MemberRoles
            .AsNoTracking()
            .Where(mr => mr.MembershipId == membership.Id)
            .Join(_db.ChannelRoles.AsNoTracking(),
                mr => mr.RoleId,
                cr => cr.Id,
                (mr, cr) => cr.Permissions)
            .ToListAsync(ct);

        var aggregated = rolePerms.Aggregate(ChannelPermission.None, (acc, p) => acc | p);

        // If muted, strip write-class bits
        var isMuted = await _db.ModerationActions
            .Where(m => m.ChannelId == channelId
                     && m.TargetUserId == userId
                     && m.Type == ModerationActionType.Mute)
            .AnyAsync(ModerationAction.IsActiveExpr(utcNow), ct);

        if (isMuted)
            aggregated &= ~WriteMask;

        return (long)aggregated;
    }
}
