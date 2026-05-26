namespace Misty.Domain.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { } // For EF Core

    public Guid Id { get; private set; }
    public Guid MessageId { get; private set; }

    public string Topic { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    // Null until the outbox relay successfully publishes the message.
    public DateTime? PublishedAt { get; private set; }

    public byte[] Version { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public static OutboxMessage Create(Guid messageId, string topic, string payload)
        => new()
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            Topic = topic,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
        };

    public void MarkPublished() => PublishedAt = DateTime.UtcNow;
}
