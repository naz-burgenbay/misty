namespace Misty.Domain.Users;

public class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Bio { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static User Create(Guid id, string username, string email, string displayName)
        => new()
        {
            Id = id,
            Username = username,
            Email = email,
            DisplayName = displayName,
        };

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void UpdateProfile(string displayName, string? bio)
    {
        DisplayName = displayName;
        Bio = bio;
    }

    public void UpdateAvatarUrl(string? avatarUrl) => AvatarUrl = avatarUrl;

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DisplayName = "[deleted]";
        Bio = null;
        AvatarUrl = null;
        PasswordHash = string.Empty;
    }
}
