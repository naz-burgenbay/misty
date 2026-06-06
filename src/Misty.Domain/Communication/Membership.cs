namespace Misty.Domain.Communication;

public class Membership
{
    private Membership() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static Membership Create(Guid id, Guid channelId, Guid userId)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
        };

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
