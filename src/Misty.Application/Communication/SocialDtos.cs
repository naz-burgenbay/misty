namespace Misty.Application.Communication;

public sealed record FriendDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    string Version);

public sealed record FriendRequestDto(
    Guid Id,
    Guid SenderId,
    string SenderUsername,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    string Version);

public sealed record SentFriendRequestDto(
    Guid Id,
    Guid ReceiverId,
    string ReceiverUsername,
    string ReceiverDisplayName,
    string? ReceiverAvatarUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    string Version);

public sealed record ChannelInviteDto(
    Guid Id,
    Guid ChannelId,
    string ChannelName,
    Guid InvitedByUserId,
    string InvitedByDisplayName,
    string Status,
    DateTime CreatedAt,
    string Version);

public sealed record InboxItemDto(
    Guid Id,
    string Type,
    Guid ActorUserId,
    string ActorDisplayName,
    string? ActorAvatarUrl,
    Guid? ReferenceId,
    object? ReferencePayload,
    bool IsActedOn,
    DateTime CreatedAt);

public sealed record InboxPageDto(
    IReadOnlyList<InboxItemDto> Items,
    string? NextCursor);
