using Microsoft.EntityFrameworkCore;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class FriendRequestRepository : IFriendRequestRepository
{
    private readonly ApplicationDbContext _db;

    public FriendRequestRepository(ApplicationDbContext db) => _db = db;

    public Task<FriendRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.FriendRequests.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<FriendRequest?> GetPendingBetweenAsync(Guid userA, Guid userB, CancellationToken ct = default)
        => _db.FriendRequests.FirstOrDefaultAsync(
            f => f.Status == FriendRequestStatus.Pending
                && ((f.SenderId == userA && f.ReceiverId == userB)
                    || (f.SenderId == userB && f.ReceiverId == userA)),
            ct);

    public async Task<IReadOnlyList<FriendRequest>> GetPendingReceivedAsync(Guid receiverId, CancellationToken ct = default)
        => await _db.FriendRequests
            .AsNoTracking()
            .Where(f => f.ReceiverId == receiverId && f.Status == FriendRequestStatus.Pending)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FriendRequest>> GetPendingSentAsync(Guid senderId, CancellationToken ct = default)
        => await _db.FriendRequests
            .AsNoTracking()
            .Where(f => f.SenderId == senderId && f.Status == FriendRequestStatus.Pending)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(FriendRequest request, CancellationToken ct = default)
    {
        await _db.FriendRequests.AddAsync(request, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(FriendRequest request, byte[] concurrencyToken, CancellationToken ct = default)
    {
        _db.Entry(request).Property(r => r.Version).OriginalValue = concurrencyToken;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
