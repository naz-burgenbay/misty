namespace Misty.Domain.Communication;

public class Conversation
{
    private Conversation() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid UserAId { get; private set; }
    public Guid UserBId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static Conversation Create(Guid id, Guid userId1, Guid userId2)
    {
        var (a, b) = userId1.CompareTo(userId2) < 0 ? (userId1, userId2) : (userId2, userId1);
        return new()
        {
            Id = id,
            UserAId = a,
            UserBId = b,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
