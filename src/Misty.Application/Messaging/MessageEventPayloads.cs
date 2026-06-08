namespace Misty.Application.Messaging;

public sealed record MessageCreatedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    DateTime CreatedAt)
{
    public string EventType { get; init; } = "MessageCreated";
}

public sealed record MessageEditedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    string Content,
    DateTime EditedAt,
    string Version)
{
    public string EventType { get; init; } = "MessageEdited";
}

public sealed record MessageDeletedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    bool IsTombstone)
{
    public string EventType { get; init; } = "MessageDeleted";
}

public sealed record ReactionAddedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid UserId,
    string EmojiCode,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = MessageEventTypes.ReactionAdded;
}

public sealed record ReactionRemovedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid UserId,
    string EmojiCode,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = MessageEventTypes.ReactionRemoved;
}

public static class MessageEventTopics
{
    public const string Message = "message-events";
}

public static class MessageEventTypes
{
    public const string MessageCreated = "MessageCreated";
    public const string MessageEdited = "MessageEdited";
    public const string MessageDeleted = "MessageDeleted";
    public const string ReactionAdded = "ReactionAdded";
    public const string ReactionRemoved = "ReactionRemoved";
}
