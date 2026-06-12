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

    public Task<User?> GetByIdForReadAsync(Guid id, CancellationToken ct = default)
        => _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _db.Users
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted, ct);

    public async Task<IReadOnlyList<User>> SearchByUsernameAsync(string query, Guid? excludeUserId, int take, CancellationToken ct = default)
    {
        var q = _db.Users.AsNoTracking().Where(u => !u.IsDeleted);
        if (excludeUserId is { } self)
            q = q.Where(u => u.Id != self);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(u => EF.Functions.Like(u.Username, $"%{query}%") || EF.Functions.Like(u.DisplayName, $"%{query}%"));
        return await q
            .OrderBy(u => u.Username)
            .Take(take)
            .ToListAsync(ct);
    }

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

    public async Task UpdateAvatarUrlAsync(User user, string? avatarUrl, byte[] concurrencyToken, CancellationToken ct = default)
    {
        user.UpdateAvatarUrl(avatarUrl);
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

    public Task<User?> GetByConfirmationTokenAsync(string token, CancellationToken ct = default)
        => _db.Users.FirstOrDefaultAsync(u => u.EmailConfirmationToken == token, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
