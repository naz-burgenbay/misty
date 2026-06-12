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
    public bool EmailConfirmed { get; private set; }
    public string? EmailConfirmationToken { get; private set; }
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

    public string GenerateConfirmationToken()
    {
        EmailConfirmationToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return EmailConfirmationToken;
    }

    public bool ConfirmEmail(string token)
    {
        if (EmailConfirmed || EmailConfirmationToken != token) return false;
        EmailConfirmed = true;
        EmailConfirmationToken = null;
        return true;
    }

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
