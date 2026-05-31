using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public interface IMessageRepository
{
    Task<Message?> FindByIdempotencyKeyAsync(Guid authorId, string idempotencyKey, CancellationToken ct = default);
    Task<Message?> GetByIdAsync(Guid messageId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Message>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<(List<Message> Messages, string? NextCursor)> GetByChannelAsync(
        Guid channelId,
        int pageSize,
        string? cursor,
        CancellationToken ct = default);
    Task<(List<Message> Messages, string? NextCursor)> GetByConversationAsync(
        Guid conversationId,
        int pageSize,
        string? cursor,
        CancellationToken ct = default);
    Task<bool> HasRepliesAsync(Guid messageId, CancellationToken ct = default);
    Task<bool> AnyForConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateAsync(Message message, CancellationToken ct = default);
    Task DeleteAsync(Message message, CancellationToken ct = default);
}
