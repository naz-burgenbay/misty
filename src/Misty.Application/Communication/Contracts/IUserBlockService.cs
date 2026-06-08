namespace Misty.Application.Communication.Contracts;

public sealed record BlockedUserDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTime BlockedAt,
    string Version);

public interface IUserBlockService
{
    Task<bool> BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    Task<bool> IsBlockedAsync(Guid userId1, Guid userId2, CancellationToken ct = default);

    Task<IReadOnlyList<BlockedUserDto>> GetBlocksAsync(Guid blockerId, CancellationToken ct = default);
}
