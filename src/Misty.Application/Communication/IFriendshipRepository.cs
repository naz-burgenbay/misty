using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IFriendshipRepository
{
    Task<Friendship?> GetForPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId1, Guid userId2, CancellationToken ct = default);
    Task<IReadOnlyList<Friend>> GetFriendsOfAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(Friendship friendship, CancellationToken ct = default);
    Task DeleteForPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default);
}

public sealed record Friend(Guid UserId, string Username, string DisplayName, string? AvatarUrl);
