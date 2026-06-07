using Microsoft.EntityFrameworkCore;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ModerationRepository : IModerationRepository
{
    private readonly ApplicationDbContext _db;

    public ModerationRepository(ApplicationDbContext db) => _db = db;

    public Task<ModerationAction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ModerationActions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<bool> HasActiveAsync(
        Guid channelId, Guid targetUserId, ModerationActionType type, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        return _db.ModerationActions
            .Where(a => a.ChannelId == channelId && a.TargetUserId == targetUserId && a.Type == type)
            .AnyAsync(ModerationAction.IsActiveExpr(utcNow), ct);
    }

    public async Task<IReadOnlyList<ModerationAction>> GetActiveForUserAsync(
        Guid channelId, Guid targetUserId, CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        // Kick is a historical event (no expiry, no revocation), not an active  sanction, so we exclude it here
        // so accumulated kicks don't pollute the active-actions list. Use a dedicated history query for kicks.
        return await _db.ModerationActions
            .AsNoTracking()
            .Where(a => a.ChannelId == channelId
                     && a.TargetUserId == targetUserId
                     && a.Type != ModerationActionType.Kick)
            .Where(ModerationAction.IsActiveExpr(utcNow))
            .ToListAsync(ct);
    }

    public async Task AddAsync(ModerationAction action, CancellationToken ct = default)
    {
        await _db.ModerationActions.AddAsync(action, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ModerationAction action, byte[] concurrencyToken, CancellationToken ct = default)
    {
        _db.Entry(action).Property(a => a.Version).OriginalValue = concurrencyToken;
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
