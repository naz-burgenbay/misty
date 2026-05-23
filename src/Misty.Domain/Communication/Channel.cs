namespace Misty.Domain.Communication;

public class Channel
{
    private Channel() { } // For EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsPrivate { get; private set; }
    public string? InviteCode { get; private set; }
    public bool IsAiAssistantEnabled { get; private set; }
    public ChannelPermission DefaultPermissions { get; private set; }
    public int MemberCount { get; private set; }
    public DateTime? LastMessageAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static Channel Create(
        Guid id,
        string name,
        bool isPrivate,
        bool isAiAssistantEnabled,
        ChannelPermission defaultPermissions,
        Guid createdByUserId)
        => new()
        {
            Id = id,
            Name = name,
            IsPrivate = isPrivate,
            InviteCode = isPrivate ? GenerateInviteCode() : null,
            IsAiAssistantEnabled = isAiAssistantEnabled,
            DefaultPermissions = defaultPermissions,
            MemberCount = 0,
            CreatedByUserId = createdByUserId,
        };

    public void Update(string name, bool isAiAssistantEnabled, ChannelPermission defaultPermissions)
    {
        Name = name;
        IsAiAssistantEnabled = isAiAssistantEnabled;
        DefaultPermissions = defaultPermissions;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }

    public void IncrementMemberCount() => MemberCount++;
    public void DecrementMemberCount() => MemberCount = Math.Max(0, MemberCount - 1);

    private static string GenerateInviteCode()
        => Guid.NewGuid().ToString("N")[..12].ToUpper();
}
