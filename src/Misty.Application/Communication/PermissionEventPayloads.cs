namespace Misty.Application.Communication;

public sealed record MembershipJoinedPayload(
    Guid MembershipId,
    Guid ChannelId,
    Guid UserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.MembershipJoined;
}

public sealed record MembershipLeftPayload(
    Guid MembershipId,
    Guid ChannelId,
    Guid UserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.MembershipLeft;
}

public sealed record MembershipKickedPayload(
    Guid MembershipId,
    Guid ChannelId,
    Guid TargetUserId,
    Guid KickedByUserId,
    Guid ModerationActionId,
    string Reason,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.MembershipKicked;
}

public sealed record MemberRoleAssignedPayload(
    Guid ChannelId,
    Guid TargetUserId,
    Guid RoleId,
    Guid AssignedByUserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.MemberRoleAssigned;
}

public sealed record MemberRoleRevokedPayload(
    Guid ChannelId,
    Guid TargetUserId,
    Guid RoleId,
    Guid RevokedByUserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.MemberRoleRevoked;
}

public sealed record ChannelRoleCreatedPayload(
    Guid ChannelId,
    Guid RoleId,
    Guid CreatedByUserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.ChannelRoleCreated;
}

public sealed record ChannelRoleUpdatedPayload(
    Guid ChannelId,
    Guid RoleId,
    Guid UpdatedByUserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.ChannelRoleUpdated;
}

public sealed record ChannelRoleDeletedPayload(
    Guid ChannelId,
    Guid RoleId,
    Guid DeletedByUserId,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.ChannelRoleDeleted;
}

public sealed record ModerationActionAppliedPayload(
    Guid ModerationActionId,
    Guid ChannelId,
    Guid TargetUserId,
    Guid IssuedByUserId,
    string ActionType,
    string Reason,
    DateTime? ExpiresAt,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.ModerationActionApplied;
}

public sealed record ModerationActionRevokedPayload(
    Guid ModerationActionId,
    Guid ChannelId,
    Guid TargetUserId,
    Guid RevokedByUserId,
    string ActionType,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = PermissionEventTypes.ModerationActionRevoked;
}

public static class PermissionEventTopics
{
    public const string Membership = "membership-events";
    public const string Role = "role-events";
    public const string Moderation = "moderation-events";
}

public static class PermissionEventTypes
{
    public const string MembershipJoined = "MembershipJoined";
    public const string MembershipLeft = "MembershipLeft";
    public const string MembershipKicked = "MembershipKicked";
    public const string MemberRoleAssigned = "MemberRoleAssigned";
    public const string MemberRoleRevoked = "MemberRoleRevoked";
    public const string ChannelRoleCreated = "ChannelRoleCreated";
    public const string ChannelRoleUpdated = "ChannelRoleUpdated";
    public const string ChannelRoleDeleted = "ChannelRoleDeleted";
    public const string ModerationActionApplied = "ModerationActionApplied";
    public const string ModerationActionRevoked = "ModerationActionRevoked";
}
