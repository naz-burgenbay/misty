namespace Misty.Domain.Communication;

public class ChannelRole
{
    private ChannelRole() { }

    public Guid Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public string Name { get; private set; } = null!;
    public ChannelPermission Permissions { get; private set; }
    public bool IsOwnerRole { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static ChannelRole Create(Guid id, Guid channelId, string name, ChannelPermission permissions)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            Name = name,
            Permissions = permissions,
        };

    public static ChannelRole CreateOwner(Guid id, Guid channelId)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            Name = "Owner",
            Permissions = ChannelPermission.All,
            IsOwnerRole = true,
        };

    public void Update(string name, ChannelPermission permissions)
    {
        Name = name;
        Permissions = permissions;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
