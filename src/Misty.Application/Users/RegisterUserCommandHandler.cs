using MediatR;
using Microsoft.AspNetCore.Identity;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Users;

namespace Misty.Application.Users;

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IOutboxWriter _outbox;

    public RegisterUserCommandHandler(IUserRepository users, IPasswordHasher<User> hasher, IOutboxWriter outbox)
    {
        _users = users;
        _hasher = hasher;
        _outbox = outbox;
    }

    public async Task<RegisterUserResponse> Handle(RegisterUserCommand cmd, CancellationToken ct)
    {
        if (await _users.UsernameExistsAsync(cmd.Username, ct))
            throw new ConflictException($"Username '{cmd.Username}' is already taken.");

        var email = cmd.Email.Trim().ToLowerInvariant();
        if (await _users.EmailExistsAsync(email, ct))
            throw new ConflictException($"Email '{email}' is already registered.");

        var user = User.Create(Guid.NewGuid(), cmd.Username, email, cmd.DisplayName);
        user.SetPasswordHash(_hasher.HashPassword(user, cmd.Password));

        _outbox.Queue(
            UserEventTopics.User,
            UserEventTypes.UserRegistered,
            user.Id,
            new UserRegisteredPayload(user.Id, user.Username, user.Email, DateTime.UtcNow));

        await _users.AddAsync(user, ct);

        return new RegisterUserResponse(user.Id);
    }
}
