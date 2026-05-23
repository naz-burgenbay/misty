using Microsoft.EntityFrameworkCore;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ChannelRepository : IChannelRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelRepository(ApplicationDbContext db) => _db = db;

    public Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Channels.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

    public async Task CreateWithOwnerAsync(
        Channel channel,
        ChannelRole ownerRole,
        Membership creatorMembership,
        MemberRole ownerMemberRole,
        CancellationToken ct = default)
    {
        await _db.Channels.AddAsync(channel, ct);
        await _db.ChannelRoles.AddAsync(ownerRole, ct);
        await _db.Memberships.AddAsync(creatorMembership, ct);
        await _db.MemberRoles.AddAsync(ownerMemberRole, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Channel channel, byte[] concurrencyToken, CancellationToken ct = default)
    {
        _db.Entry(channel).Property(c => c.Version).OriginalValue = concurrencyToken;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    public async Task SoftDeleteAsync(Channel channel, CancellationToken ct = default)
    {
        channel.SoftDelete();
        await _db.SaveChangesAsync(ct);
    }
}
