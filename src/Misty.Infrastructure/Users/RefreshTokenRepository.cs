using Microsoft.EntityFrameworkCore;
using Misty.Application.Users;
using Misty.Domain.Users;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Users;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _db;

    public RefreshTokenRepository(ApplicationDbContext db) => _db = db;

    public Task<RefreshToken?> GetByHashWithUserAsync(string tokenHash, CancellationToken ct = default)
        => _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        await _db.RefreshTokens.AddAsync(token, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RotateAsync(RefreshToken old, RefreshToken newToken, CancellationToken ct = default)
    {
        old.Revoke();
        await _db.RefreshTokens.AddAsync(newToken, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is null || existing.RevokedAt is not null)
            return;
        existing.Revoke();
        await _db.SaveChangesAsync(ct);
    }
}
