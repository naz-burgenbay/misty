namespace Misty.Domain.Communication;

public class ModerationAction
{
    private ModerationAction() { }

    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public ModerationActionType Type { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public static ModerationAction Create(
        Guid id,
        Guid channelId,
        Guid targetUserId,
        Guid issuedByUserId,
        ModerationActionType type,
        DateTime? expiresAt = null)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            TargetUserId = targetUserId,
            IssuedByUserId = issuedByUserId,
            Type = type,
            ExpiresAt = expiresAt,
        };

    // Returns true if this action is currently active (not revoked, not expired)
    public bool IsActive(DateTime utcNow)
        => RevokedAt is null && (ExpiresAt is null || ExpiresAt > utcNow);

    public void Revoke(DateTime revokedAt) => RevokedAt = revokedAt;
}
