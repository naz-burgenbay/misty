using Misty.Domain.Users;

namespace Misty.Application.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, byte[] concurrencyToken, CancellationToken ct = default);
    Task UpdateAvatarUrlAsync(User user, string avatarUrl, CancellationToken ct = default);
    Task SoftDeleteAsync(User user, CancellationToken ct = default);
}
