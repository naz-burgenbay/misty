using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ChannelRoleRepository : IChannelRoleRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelRoleRepository(ApplicationDbContext db) => _db = db;

    public Task<ChannelRole?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ChannelRoles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<ChannelRole>> GetByChannelIdAsync(Guid channelId, CancellationToken ct = default)
        => _db.ChannelRoles.Where(r => r.ChannelId == channelId).ToListAsync(ct);

    public async Task AddAsync(ChannelRole role, CancellationToken ct = default)
    {
        await _db.ChannelRoles.AddAsync(role, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ChannelRole role, CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ChannelRole role, CancellationToken ct = default)
    {
        _db.ChannelRoles.Remove(role);
        await _db.SaveChangesAsync(ct);
    }
}
