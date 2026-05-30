using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IFriendRequestRepository
{
    Task<FriendRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FriendRequest?> GetPendingBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default);
    Task<IReadOnlyList<FriendRequest>> GetPendingReceivedAsync(Guid receiverId, CancellationToken ct = default);
    Task AddAsync(FriendRequest request, CancellationToken ct = default);
    Task UpdateAsync(FriendRequest request, CancellationToken ct = default);
}
