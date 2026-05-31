using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public interface IReactionRepository
{
    Task<MessageReaction?> GetAsync(Guid messageId, Guid userId, string emojiCode, CancellationToken ct = default);

    Task AddAsync(MessageReaction reaction, CancellationToken ct = default);

    Task RemoveAsync(MessageReaction reaction, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReactionAggregate>>> GetAggregatesAsync(
        IReadOnlyCollection<Guid> messageIds,
        Guid currentUserId,
        CancellationToken ct = default);
}

public sealed record ReactionAggregate(string EmojiCode, int Count, bool ReactedByMe);
