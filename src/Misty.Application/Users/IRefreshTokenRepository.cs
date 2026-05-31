using Misty.Domain.Users;

namespace Misty.Application.Users;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashWithUserAsync(string tokenHash, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task RotateAsync(RefreshToken old, RefreshToken newToken, CancellationToken ct = default);
    Task RevokeByHashAsync(string tokenHash, CancellationToken ct = default);
}
