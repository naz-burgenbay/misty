using MediatR;
using Microsoft.AspNetCore.Identity;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Users;

namespace Misty.Application.Users;

internal sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshTokens;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher<User> hasher,
        ITokenService tokens,
        IRefreshTokenRepository refreshTokens)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _refreshTokens = refreshTokens;
    }

    public async Task<LoginResponse> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(cmd.Username, ct);

        if (user is null)
            throw new UnauthorizedException();

        if (!user.EmailConfirmed)
            throw new UnauthorizedException("Please confirm your email address before signing in.");

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, cmd.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedException();

        var accessToken = _tokens.CreateAccessToken(user);
        var (refreshTokenPlaintext, refreshTokenHash, expiresAt) = _tokens.CreateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, refreshTokenHash, expiresAt);
        await _refreshTokens.AddAsync(refreshToken, ct);

        return new LoginResponse(accessToken, refreshTokenPlaintext, user.Id);
    }
}
