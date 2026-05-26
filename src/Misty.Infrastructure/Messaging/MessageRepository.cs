using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _db;

    public MessageRepository(ApplicationDbContext db) => _db = db;

    public Task<Message?> FindByIdempotencyKeyAsync(Guid authorId, string idempotencyKey, CancellationToken ct = default)
        => _db.Messages.FirstOrDefaultAsync(
            m => m.AuthorId == authorId && m.IdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        // Serialize the MessageCreated event payload for the outbox.
        var payload = JsonSerializer.Serialize(new MessageCreatedPayload(
            message.Id,
            message.ChannelId,
            message.ConversationId,
            message.AuthorId,
            message.Content,
            message.ParentMessageId,
            message.CreatedAt));

        var outbox = OutboxMessage.Create(message.Id, "message-events", payload);

        // Both rows are written in one SaveChangesAsync call for a single SQL transaction.
        await _db.Messages.AddAsync(message, ct);
        await _db.OutboxMessages.AddAsync(outbox, ct);
        await _db.SaveChangesAsync(ct);
    }
}

// Payload written to msg.OutboxMessage and later published to the message-events Service Bus topic.
internal sealed record MessageCreatedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    DateTime CreatedAt);

