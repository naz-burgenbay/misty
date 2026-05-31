using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly ApplicationDbContext _db;

    public FriendshipRepository(ApplicationDbContext db) => _db = db;

    public Task<Friendship?> GetForPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
    {
        var (a, b) = Canonical(userId1, userId2);
        return _db.Friendships.FirstOrDefaultAsync(f => f.UserAId == a && f.UserBId == b, ct);
    }

    public Task<bool> ExistsAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
    {
        var (a, b) = Canonical(userId1, userId2);
        return _db.Friendships.AnyAsync(f => f.UserAId == a && f.UserBId == b, ct);
    }

    public async Task<IReadOnlyList<Friend>> GetFriendsOfAsync(Guid userId, CancellationToken ct = default)
    {
        var query =
            from f in _db.Friendships.AsNoTracking()
            where f.UserAId == userId || f.UserBId == userId
            let otherId = f.UserAId == userId ? f.UserBId : f.UserAId
            join u in _db.Users.AsNoTracking() on otherId equals u.Id
            where !u.IsDeleted
                && !_db.UserBlocks.Any(b => b.BlockerId == userId && b.BlockedId == otherId)
                && !_db.UserBlocks.Any(b => b.BlockerId == otherId && b.BlockedId == userId)
            orderby u.DisplayName
            select new Friend(u.Id, u.Username, u.DisplayName, u.AvatarUrl);

        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(Friendship friendship, CancellationToken ct = default)
    {
        await _db.Friendships.AddAsync(friendship, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteForPairAsync(Guid userId1, Guid userId2, CancellationToken ct = default)
    {
        var (a, b) = Canonical(userId1, userId2);
        await _db.Friendships
            .Where(f => f.UserAId == a && f.UserBId == b)
            .ExecuteDeleteAsync(ct);
    }

    private static (Guid A, Guid B) Canonical(Guid x, Guid y)
        => new System.Data.SqlTypes.SqlGuid(x).CompareTo(new System.Data.SqlTypes.SqlGuid(y)) < 0 ? (x, y) : (y, x);
}
