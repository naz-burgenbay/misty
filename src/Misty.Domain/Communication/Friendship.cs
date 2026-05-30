namespace Misty.Domain.Communication;

public class Friendship
{
    private Friendship() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid UserAId { get; private set; }
    public Guid UserBId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Friendship Create(Guid id, Guid userId1, Guid userId2)
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
}
