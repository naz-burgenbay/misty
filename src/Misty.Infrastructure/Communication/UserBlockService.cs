using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class UserBlockService : IUserBlockService
{
    private readonly ApplicationDbContext _db;
    private readonly IFriendshipRepository _friendships;

    public UserBlockService(ApplicationDbContext db, IFriendshipRepository friendships)
    {
        _db = db;
        _friendships = friendships;
    }

    public async Task<bool> BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
    {
        var exists = await _db.UserBlocks.AnyAsync(
            b => b.BlockerId == blockerId && b.BlockedId == blockedId, ct);
        if (exists) return false;

        await _db.UserBlocks.AddAsync(UserBlock.Create(blockerId, blockedId), ct);
        await _db.SaveChangesAsync(ct);

        await _friendships.DeleteForPairAsync(blockerId, blockedId, ct);
        return true;
    }

    public async Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
    {
        var block = await _db.UserBlocks.FirstOrDefaultAsync(
            b => b.BlockerId == blockerId && b.BlockedId == blockedId, ct);
        if (block is null) return false;

        _db.UserBlocks.Remove(block);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> IsBlockedAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
        => _db.UserBlocks.AnyAsync(
            b => (b.BlockerId == userId1 && b.BlockedId == userId2)
              || (b.BlockerId == userId2 && b.BlockedId == userId1),
            ct);

    public async Task<IReadOnlyList<BlockedUserDto>> GetBlocksAsync(Guid blockerId, CancellationToken ct = default)
    {
        return await (
            from b in _db.UserBlocks.AsNoTracking()
            where b.BlockerId == blockerId
            join u in _db.Users.AsNoTracking() on b.BlockedId equals u.Id
            orderby b.CreatedAt descending
            select new BlockedUserDto(u.Id, u.Username, u.DisplayName, u.AvatarUrl, b.CreatedAt, Convert.ToBase64String(b.Version)))
            .ToListAsync(ct);
    }
}
