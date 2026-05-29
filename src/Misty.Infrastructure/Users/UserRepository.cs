using Microsoft.EntityFrameworkCore;
using Misty.Application.Common.Exceptions;
using Misty.Application.Users;
using Misty.Domain.Users;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Users;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _db.Users
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted, ct);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Username == username, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _db.Users.AddAsync(user, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, byte[] concurrencyToken, CancellationToken ct = default)
    {
        _db.Entry(user).Property(u => u.Version).OriginalValue = concurrencyToken;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    public async Task UpdateAvatarUrlAsync(User user, string avatarUrl, CancellationToken ct = default)
    {
        user.UpdateAvatarUrl(avatarUrl);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(User user, CancellationToken ct = default)
    {
        user.SoftDelete();
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens)
            token.Revoke();
        await _db.SaveChangesAsync(ct);
    }
}
