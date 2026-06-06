namespace Misty.Application.Communication.Contracts;

public sealed record BlockedUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTime BlockedAt,
    string Version);

// Manages block relationships between users and exposes block lookups to permission checks across Communication and Messaging modules.
public interface IUserBlockService
{
    // Returns true when a new block row was created, false when the block already existed.
    Task<bool> BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    // Returns true when an existing block row was removed, false when no row matched.
    Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    // Returns true if either user has blocked the other (bidirectional check)
    Task<bool> IsBlockedAsync(Guid userId1, Guid userId2, CancellationToken ct = default);

    Task<IReadOnlyList<BlockedUserDto>> GetBlocksAsync(Guid blockerId, CancellationToken ct = default);
}
