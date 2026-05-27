using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class ReactionRepository : IReactionRepository
{
    private readonly ApplicationDbContext _db;

    public ReactionRepository(ApplicationDbContext db) => _db = db;

    public Task<MessageReaction?> GetAsync(Guid messageId, Guid userId, string emojiCode, CancellationToken ct = default)
        => _db.MessageReactions.FirstOrDefaultAsync(
            r => r.MessageId == messageId && r.UserId == userId && r.EmojiCode == emojiCode, ct);

    public async Task AddAsync(MessageReaction reaction, Guid? channelId, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new ReactionChangedPayload(
            reaction.MessageId,
            channelId,
            reaction.UserId,
            reaction.EmojiCode,
            "added",
            DateTime.UtcNow));

        var outbox = OutboxMessage.Create(reaction.MessageId, "message-events", "ReactionChanged", payload);

        await _db.MessageReactions.AddAsync(reaction, ct);
        await _db.OutboxMessages.AddAsync(outbox, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(MessageReaction reaction, Guid? channelId, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new ReactionChangedPayload(
            reaction.MessageId,
            channelId,
            reaction.UserId,
            reaction.EmojiCode,
            "removed",
            DateTime.UtcNow));

        var outbox = OutboxMessage.Create(reaction.MessageId, "message-events", "ReactionChanged", payload);

        _db.MessageReactions.Remove(reaction);
        await _db.OutboxMessages.AddAsync(outbox, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReactionAggregate>>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> messageIds,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<ReactionAggregate>>();

        // Pull all reactions for the requested messages in a single round-trip, then group in memory.
        var rows = await _db.MessageReactions
            .Where(r => messageIds.Contains(r.MessageId))
            .Select(r => new { r.MessageId, r.UserId, r.EmojiCode })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.MessageId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ReactionAggregate>)g
                    .GroupBy(r => r.EmojiCode)
                    .Select(eg => new ReactionAggregate(
                        eg.Key,
                        eg.Count(),
                        eg.Any(r => r.UserId == currentUserId)))
                    .ToList());
    }
}

// Payload written to msg.OutboxMessage and published to the message-events Service Bus topic with Subject="ReactionChanged". Action is "added" or "removed".
public sealed record ReactionChangedPayload(
    Guid MessageId,
    Guid? ChannelId,
    Guid UserId,
    string EmojiCode,
    string Action,
    DateTime OccurredAt)
{
    public string EventType { get; init; } = "ReactionChanged";
}
