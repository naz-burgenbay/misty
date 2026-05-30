namespace Misty.Domain.Communication;

public enum FriendRequestStatus
{
    Pending,
    Accepted,
    Declined,
}

public class FriendRequest
{
    private FriendRequest() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid ReceiverId { get; private set; }
    public FriendRequestStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }

    public static FriendRequest Create(Guid id, Guid senderId, Guid receiverId)
        => new()
        {
            Id = id,
            SenderId = senderId,
            ReceiverId = receiverId,
            Status = FriendRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

    public void Accept()
    {
        Status = FriendRequestStatus.Accepted;
        RespondedAt = DateTime.UtcNow;
    }

    public void Decline()
    {
        Status = FriendRequestStatus.Declined;
        RespondedAt = DateTime.UtcNow;
    }
}
