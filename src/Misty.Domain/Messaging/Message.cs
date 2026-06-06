namespace Misty.Domain.Messaging;

public class Message
{
    private Message() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid? ChannelId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Content { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? ParentMessageId { get; private set; }
    public DateTime? EditedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static Message CreateForChannel(
        Guid id,
        Guid channelId,
        Guid authorId,
        string content,
        string idempotencyKey,
        Guid? parentMessageId = null)
        => new()
        {
            Id = id,
            ChannelId = channelId,
            ConversationId = null,
            AuthorId = authorId,
            Content = content,
            IdempotencyKey = idempotencyKey,
            ParentMessageId = parentMessageId,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        };

    public static Message CreateForConversation(
        Guid id,
        Guid conversationId,
        Guid authorId,
        string content,
        string idempotencyKey,
        Guid? parentMessageId = null)
        => new()
        {
            Id = id,
            ChannelId = null,
            ConversationId = conversationId,
            AuthorId = authorId,
            Content = content,
            IdempotencyKey = idempotencyKey,
            ParentMessageId = parentMessageId,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        };

    public void Edit(string newContent)
    {
        Content = newContent;
        EditedAt = DateTime.UtcNow;
    }

    public void Tombstone()
    {
        Content = string.Empty;
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
    }
}
