using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Conversation?> GetByUsersAsync(Guid userAId, Guid userBId, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetForUserAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Conversation conversation, CancellationToken ct = default);
}
