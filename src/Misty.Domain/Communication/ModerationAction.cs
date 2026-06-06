namespace Misty.Domain.Communication;

using System.Linq.Expressions;

public class ModerationAction
{
    private ModerationAction() { }

    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public ModerationActionType Type { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public static ModerationAction Create(
        Guid id,
        Guid channelId,
        Guid targetUserId,
        Guid issuedByUserId,
        ModerationActionType type,
        string reason,
        DateTime? expiresAt = null)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            TargetUserId = targetUserId,
            IssuedByUserId = issuedByUserId,
            Type = type,
            Reason = reason,
            ExpiresAt = expiresAt,
        };

    // Returns true if this action is currently active (not revoked, not expired)
    public bool IsActive(DateTime utcNow)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > utcNow);

    // EF-translatable form of IsActive. The single source of truth for the "active moderation action" predicate.
    // Both in-memory checks and SQL queries must go through this.
    public static Expression<Func<ModerationAction, bool>> IsActiveExpr(DateTime utcNow)
        => a => a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > utcNow);

    public void Revoke(DateTime revokedAt) => RevokedAt = revokedAt;

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
