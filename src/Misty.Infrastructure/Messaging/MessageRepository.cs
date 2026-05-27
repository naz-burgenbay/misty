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

    public Task<Message?> GetByIdAsync(Guid messageId, CancellationToken ct = default)
        => _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task<IReadOnlyDictionary<Guid, Message>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, Message>();

        var list = await _db.Messages
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);

        return list.ToDictionary(m => m.Id);
    }

    public async Task<(List<Message> Messages, string? NextCursor)> GetByChannelAsync(
        Guid channelId,
        int pageSize,
        string? cursor,
        CancellationToken ct = default)
    {
        IQueryable<Message> query = _db.Messages.Where(m => m.ChannelId == channelId);

        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split('|');
            if (parts.Length == 2 &&
                DateTime.TryParse(parts[0], out var cursorTime) &&
                Guid.TryParse(parts[1], out var cursorId))
            {
                query = query.Where(m =>
                    m.CreatedAt < cursorTime ||
                    (m.CreatedAt == cursorTime && m.Id.CompareTo(cursorId) < 0));
            }
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (messages.Count > pageSize)
        {
            var last = messages[pageSize - 1];
            nextCursor = $"{last.CreatedAt:O}|{last.Id}";
            messages = messages.Take(pageSize).ToList();
        }

        return (messages, nextCursor);
    }

    public Task<bool> HasRepliesAsync(Guid messageId, CancellationToken ct = default)
        => _db.Messages.AnyAsync(m => m.ParentMessageId == messageId, ct);

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

        var outbox = OutboxMessage.Create(message.Id, "message-events", "MessageCreated", payload);

        // Both rows are written in one SaveChangesAsync call for a single SQL transaction.
        await _db.Messages.AddAsync(message, ct);
        await _db.OutboxMessages.AddAsync(outbox, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Message message, CancellationToken ct = default)
    {
        _db.Messages.Update(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Message message, CancellationToken ct = default)
    {
        _db.Messages.Remove(message);
        await _db.SaveChangesAsync(ct);
    }
}

// Payload written to msg.OutboxMessage and later published to the message-events Service Bus topic.
// Public so that consumers in Misty.Api (e.g. RealtimeDeliveryWorker) can deserialize it directly.
public sealed record MessageCreatedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid? ConversationId,
    Guid AuthorId,
    string Content,
    Guid? ParentMessageId,
    DateTime CreatedAt)
{
    // Discriminator included in the JSON envelope for forward-compatible polymorphic deserialization.
    public string EventType { get; init; } = "MessageCreated";
}

