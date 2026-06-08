using System.Security.Cryptography;
using System.Text;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Users;

namespace Misty.Application.Users;

internal sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, RefreshResponse>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService _tokens;

    public RefreshCommandHandler(IRefreshTokenRepository refreshTokens, ITokenService tokens)
    {
        _refreshTokens = refreshTokens;
        _tokens = tokens;
    }

    public async Task<RefreshResponse> Handle(RefreshCommand cmd, CancellationToken ct)
    {
        var tokenHash = Hash(cmd.RefreshToken);
        var existing = await _refreshTokens.GetByHashWithUserAsync(tokenHash, ct);

        if (existing is null || !existing.IsActive)
            throw new UnauthorizedException();

        var newAccessToken = _tokens.CreateAccessToken(existing.User);
        var (newRefreshTokenPlaintext, newRefreshTokenHash, expiresAt) = _tokens.CreateRefreshToken();
        var newRefreshToken = RefreshToken.Create(existing.UserId, newRefreshTokenHash, expiresAt);

        await _refreshTokens.RotateAsync(existing, newRefreshToken, ct);

        return new RefreshResponse(newAccessToken, newRefreshTokenPlaintext);
    }

    internal static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
