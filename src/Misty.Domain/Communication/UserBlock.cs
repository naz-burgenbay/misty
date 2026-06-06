namespace Misty.Domain.Communication;

public class UserBlock
{
    private UserBlock() { } // For EF Core

    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static UserBlock Create(Guid blockerId, Guid blockedId)
        => new() { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = DateTime.UtcNow };
}
