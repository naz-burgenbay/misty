using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IInboxItemRepository
{
    Task<InboxItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InboxItem?> GetByReferenceAsync(Guid userId, Guid referenceId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, InboxItemType type, Guid? referenceId, CancellationToken ct = default);
    Task<(IReadOnlyList<InboxItem> Items, string? NextCursor)> GetPageAsync(Guid userId, string? cursor, int take, CancellationToken ct = default);
    Task AddAsync(InboxItem item, CancellationToken ct = default);
    Task UpdateAsync(InboxItem item, CancellationToken ct = default);
}
