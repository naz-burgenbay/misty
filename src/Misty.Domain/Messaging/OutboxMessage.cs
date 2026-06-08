namespace Misty.Domain.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }

    public string Topic { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTime? PublishedAt { get; private set; }

    public byte[] Version { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public static OutboxMessage Create(Guid messageId, string topic, string eventType, string payload)
        => new()
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Topic = topic,
            EventType = eventType,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
        };

    public void MarkPublished() => PublishedAt = DateTime.UtcNow;
}
