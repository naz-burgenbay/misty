using Microsoft.EntityFrameworkCore;
using Misty.Application.Common.Exceptions;
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

    public Task<(List<Message> Messages, string? NextCursor)> GetByChannelAsync(
        Guid channelId,
        int pageSize,
        string? cursor,
        CancellationToken ct = default)
        => PageAsync(_db.Messages.Where(m => m.ChannelId == channelId), pageSize, cursor, ct);

    public Task<(List<Message> Messages, string? NextCursor)> GetByConversationAsync(
        Guid conversationId,
        int pageSize,
        string? cursor,
        CancellationToken ct = default)
        => PageAsync(_db.Messages.Where(m => m.ConversationId == conversationId), pageSize, cursor, ct);

    private static async Task<(List<Message> Messages, string? NextCursor)> PageAsync(
        IQueryable<Message> query,
        int pageSize,
        string? cursor,
        CancellationToken ct)
    {
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

    public Task<bool> AnyForConversationAsync(Guid conversationId, CancellationToken ct = default)
        => _db.Messages.AnyAsync(m => m.ConversationId == conversationId, ct);

    // The handler is expected to queue the matching MessageCreated outbox row onto the same scoped DbContext before calling AddAsync; SaveChanges commits both in one SQL transaction.
    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await _db.Messages.AddAsync(message, ct);
        await _db.SaveChangesAsync(ct);
    }

    // The handler is expected to queue a MessageEdited or MessageDeleted (tombstone) outbox row onto the same DbContext before calling UpdateAsync.
    public async Task UpdateAsync(Message message, byte[] concurrencyToken, CancellationToken ct = default)
    {
        _db.Entry(message).Property(m => m.Version).OriginalValue = concurrencyToken;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    // The handler is expected to queue a MessageDeleted outbox row onto the same DbContext before calling DeleteAsync.
    public async Task DeleteAsync(Message message, CancellationToken ct = default)
    {
        _db.Messages.Remove(message);
        await _db.SaveChangesAsync(ct);
    }
}

