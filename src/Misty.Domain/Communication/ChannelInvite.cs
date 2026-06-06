namespace Misty.Domain.Communication;

public enum ChannelInviteStatus
{
    Pending,
    Accepted,
    Declined,
}

public class ChannelInvite
{
    private ChannelInvite() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid InvitedUserId { get; private set; }
    public ChannelInviteStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static ChannelInvite Create(Guid id, Guid channelId, Guid invitedByUserId, Guid invitedUserId)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            InvitedByUserId = invitedByUserId,
            InvitedUserId = invitedUserId,
            Status = ChannelInviteStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

    public void Accept()
    {
        Status = ChannelInviteStatus.Accepted;
        RespondedAt = DateTime.UtcNow;
    }

    public void Decline()
    {
        Status = ChannelInviteStatus.Declined;
        RespondedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
