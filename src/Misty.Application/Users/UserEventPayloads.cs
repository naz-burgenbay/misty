namespace Misty.Application.Users;

public sealed record UserRegisteredPayload(
    Guid UserId,
    string Username,
    string Email,
    DateTime OccurredAt);

public sealed record UserProfileUpdatedPayload(
    Guid UserId,
    string DisplayName,
    string? Bio,
    DateTime OccurredAt);

public sealed record UserAvatarChangedPayload(
    Guid UserId,
    string? AvatarUrl,
    DateTime OccurredAt);

public sealed record UserDeletedPayload(
    Guid UserId,
    DateTime OccurredAt);

public static class UserEventTopics
{
    public const string User = "user-events";
}

public static class UserEventTypes
{
    public const string UserRegistered = "UserRegistered";
    public const string UserProfileUpdated = "UserProfileUpdated";
    public const string UserAvatarChanged = "UserAvatarChanged";
    public const string UserDeleted = "UserDeleted";
}
