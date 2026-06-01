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
    public const string ChannelInviteSent = "ChannelInviteSent";
    public const string ChannelInviteAccepted = "ChannelInviteAccepted";
    public const string ConversationStarted = "ConversationStarted";
}
