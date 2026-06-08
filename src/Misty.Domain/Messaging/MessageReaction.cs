namespace Misty.Domain.Messaging;

public sealed class MessageReaction
{
    private MessageReaction() { }

    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public string EmojiCode { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    public static MessageReaction Create(Guid messageId, Guid userId, string emojiCode)
        => new()
        {
            MessageId = messageId,
            UserId = userId,
            EmojiCode = emojiCode,
            CreatedAt = DateTime.UtcNow,
        };
}
