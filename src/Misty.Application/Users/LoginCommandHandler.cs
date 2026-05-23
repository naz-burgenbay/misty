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

    public LoginCommandHandler(IUserRepository users, IPasswordHasher<User> hasher, ITokenService tokens)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<LoginResponse> Handle(LoginCommand cmd, CancellationToken ct)
    {
        var user = await _users.GetByUsernameAsync(cmd.Username, ct);

        if (user is null)
            throw new UnauthorizedException();

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, cmd.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedException();

        var token = _tokens.CreateAccessToken(user);
        return new LoginResponse(token, user.Id);
    }
}
