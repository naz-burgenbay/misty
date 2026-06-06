using Misty.Web.Services.Common;

namespace Misty.Web.Services.Realtime;

public enum HubConnectionState { Disconnected, Connecting, Connected, Reconnecting }

public sealed record MessageCreatedEvent(Guid MessageId, Guid? ChannelId, Guid? ConversationId,
    Guid AuthorId, string Content, Guid? ParentMessageId, DateTime CreatedAt);

public sealed record MessageEditedEvent(Guid MessageId, Guid? ChannelId, Guid? ConversationId,
    string Content, DateTime EditedAt);

public sealed record MessageDeletedEvent(Guid MessageId, Guid? ChannelId, Guid? ConversationId,
    bool IsTombstone);

public sealed record ReactionAddedEvent(Guid MessageId, Guid? ChannelId, Guid UserId,
    string EmojiCode, DateTime OccurredAt);

public sealed record ReactionRemovedEvent(Guid MessageId, Guid? ChannelId, Guid UserId,
    string EmojiCode, DateTime OccurredAt);

public sealed record PermissionInvalidationEvent(Guid? UserId, Guid ChannelId);

public sealed record PresenceChangedEvent(Guid UserId, bool IsOnline, DateTime OccurredAt);

// Single hub per tab owned by the client. Group membership is established server-side in MistyHub.OnConnectedAsync from the DB; the client never calls join/leave. After a MembershipChanged event the client forces a reconnect (StartAsync after StopAsync) so the server re-reads memberships.
public interface ISignalRClient
{
    Observable<HubConnectionState> State { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    // Fires after the connection comes back up following any disconnect (initial connect doesn't fire). MessageStore subscribes in Step 5.3 to refetch missed messages via the cursor API.
    event Action? Reconnected;

    IDisposable OnMessageCreated(Action<MessageCreatedEvent> handler);
    IDisposable OnMessageEdited(Action<MessageEditedEvent> handler);
    IDisposable OnMessageDeleted(Action<MessageDeletedEvent> handler);
    IDisposable OnReactionAdded(Action<ReactionAddedEvent> handler);
    IDisposable OnReactionRemoved(Action<ReactionRemovedEvent> handler);
    IDisposable OnMembershipChanged(Action<PermissionInvalidationEvent> handler);
    IDisposable OnRoleChanged(Action<PermissionInvalidationEvent> handler);
    IDisposable OnModerationActionApplied(Action<PermissionInvalidationEvent> handler);
    IDisposable OnPresenceChanged(Action<PresenceChangedEvent> handler);
}
