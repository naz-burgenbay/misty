using System.Security.Cryptography;
using System.Text;
using MediatR;

namespace Misty.Application.Users;

public record RevokeRefreshTokenCommand(string RefreshToken) : IRequest;

internal sealed class RevokeRefreshTokenCommandHandler : IRequestHandler<RevokeRefreshTokenCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;

    public RevokeRefreshTokenCommandHandler(IRefreshTokenRepository refreshTokens)
        => _refreshTokens = refreshTokens;

    public async Task Handle(RevokeRefreshTokenCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RefreshToken))
            return;

        var hash = Hash(cmd.RefreshToken);
        await _refreshTokens.RevokeByHashAsync(hash, ct);
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
