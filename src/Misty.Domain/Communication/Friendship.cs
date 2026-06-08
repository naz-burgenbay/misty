using System.Data.SqlTypes;

namespace Misty.Domain.Communication;

public class Friendship
{
    private Friendship() { }

    public Guid Id { get; private set; }
    public Guid UserAId { get; private set; }
    public Guid UserBId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static Friendship Create(Guid id, Guid userId1, Guid userId2)
    {
        var (a, b) = new SqlGuid(userId1).CompareTo(new SqlGuid(userId2)) < 0
            ? (userId1, userId2)
            : (userId2, userId1);
        return new()
        {
            Id = id,
            UserAId = a,
            UserBId = b,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
