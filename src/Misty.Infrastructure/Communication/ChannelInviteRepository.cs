using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ChannelInviteRepository : IChannelInviteRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelInviteRepository(ApplicationDbContext db) => _db = db;

    public Task<ChannelInvite?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ChannelInvites.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<ChannelInvite?> GetPendingAsync(Guid channelId, Guid invitedUserId, CancellationToken ct = default)
        => _db.ChannelInvites.FirstOrDefaultAsync(
            i => i.ChannelId == channelId
                && i.InvitedUserId == invitedUserId
                && i.Status == ChannelInviteStatus.Pending,
            ct);

    public async Task AddAsync(ChannelInvite invite, CancellationToken ct = default)
    {
        await _db.ChannelInvites.AddAsync(invite, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(ChannelInvite invite, CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
