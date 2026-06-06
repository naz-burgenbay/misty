namespace Misty.Application.Communication;

public sealed record FriendRequestSentPayload(
    Guid RequestId,
    Guid SenderId,
    Guid ReceiverId,
    DateTime OccurredAt);

public sealed record FriendRequestAcceptedPayload(
    Guid RequestId,
    Guid AccepterId,
    Guid OriginalSenderId,
    DateTime OccurredAt);

public sealed record FriendRequestDeclinedPayload(
    Guid RequestId,
    Guid DeclinedByUserId,
    Guid OriginalSenderId,
    DateTime OccurredAt);

public sealed record FriendRequestCancelledPayload(
    Guid RequestId,
    Guid CancelledByUserId,
    Guid ReceiverId,
    DateTime OccurredAt);

public sealed record FriendshipCreatedPayload(
    Guid FriendshipId,
    Guid UserAId,
    Guid UserBId,
    Guid AccepterId,
    DateTime OccurredAt);

public sealed record FriendshipRemovedPayload(
    Guid FriendshipId,
    Guid UserAId,
    Guid UserBId,
    Guid RemovedByUserId,
    DateTime OccurredAt);

public sealed record ChannelInviteSentPayload(
    Guid InviteId,
    Guid ChannelId,
    Guid InvitedByUserId,
    Guid InvitedUserId,
    DateTime OccurredAt);

public sealed record ChannelInviteAcceptedPayload(
    Guid InviteId,
    Guid ChannelId,
    Guid AccepterId,
    Guid OriginalInviterId,
    DateTime OccurredAt);

public sealed record ChannelInviteDeclinedPayload(
    Guid InviteId,
    Guid ChannelId,
    Guid InvitedUserId,
    Guid OriginalInviterId,
    DateTime OccurredAt);

// Reserved for a future cancel-by-inviter command (no handler emits this today).
public sealed record ChannelInviteCancelledPayload(
    Guid InviteId,
    Guid ChannelId,
    Guid CancelledByUserId,
    Guid InvitedUserId,
    DateTime OccurredAt);

public sealed record ConversationStartedPayload(
    Guid ConversationId,
    Guid SenderId,
    Guid RecipientId,
    DateTime OccurredAt);

public static class SocialEventTopics
{
    public const string Friend = "friend-events";
    public const string ChannelInvite = "channel-invite-events";
    public const string Message = "message-events";
}

public static class SocialEventTypes
{
    public const string FriendRequestSent = "FriendRequestSent";
    public const string FriendRequestAccepted = "FriendRequestAccepted";
    public const string FriendRequestDeclined = "FriendRequestDeclined";
    public const string FriendRequestCancelled = "FriendRequestCancelled";
    public const string FriendshipCreated = "FriendshipCreated";
    public const string FriendshipRemoved = "FriendshipRemoved";
    public const string ChannelInviteSent = "ChannelInviteSent";
    public const string ChannelInviteAccepted = "ChannelInviteAccepted";
    public const string ChannelInviteDeclined = "ChannelInviteDeclined";
    public const string ChannelInviteCancelled = "ChannelInviteCancelled";
    public const string ConversationStarted = "ConversationStarted";
}
