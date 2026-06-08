using Misty.Application.Communication.Contracts;

namespace Misty.Infrastructure.Communication;

public sealed class StubUserBlockService : IUserBlockService
{
    public Task<bool> BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> IsBlockedAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<BlockedUserDto>> GetBlocksAsync(Guid blockerId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BlockedUserDto>>(Array.Empty<BlockedUserDto>());
}
