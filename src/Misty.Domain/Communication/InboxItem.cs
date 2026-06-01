namespace Misty.Domain.Communication;

public enum InboxItemType
{
    FriendRequestReceived,
    FriendRequestAccepted,
    ChannelInviteReceived,
    ChannelInviteAccepted,
    ConversationStarted,
}

public class InboxItem
{
    private InboxItem() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public InboxItemType Type { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public bool IsActedOn { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static InboxItem Create(
        Guid id,
        Guid userId,
        InboxItemType type,
        Guid actorUserId,
        Guid? referenceId)
        => new()
        {
            Id = id,
            UserId = userId,
            Type = type,
            ActorUserId = actorUserId,
            ReferenceId = referenceId,
            IsActedOn = false,
            CreatedAt = DateTime.UtcNow,
        };

    public void MarkActedOn() => IsActedOn = true;
}
