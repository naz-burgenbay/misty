using MediatR;
using Microsoft.AspNetCore.Identity;
using Misty.Application.Common;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Users;

namespace Misty.Application.Users;

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IOutboxWriter _outbox;
    private readonly IEmailService _email;
    private readonly IAppSettings _appSettings;

    public RegisterUserCommandHandler(
        IUserRepository users,
        IPasswordHasher<User> hasher,
        IOutboxWriter outbox,
        IEmailService email,
        IAppSettings appSettings)
    {
        _users = users;
        _hasher = hasher;
        _outbox = outbox;
        _email = email;
        _appSettings = appSettings;
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
        var token = user.GenerateConfirmationToken();

        _outbox.Queue(
            UserEventTopics.User,
            UserEventTypes.UserRegistered,
            user.Id,
            new UserRegisteredPayload(user.Id, user.Username, user.Email, DateTime.UtcNow));

        await _users.AddAsync(user, ct);

        var baseUrl = _appSettings.AppBaseUrl.TrimEnd('/');
        var confirmUrl = $"{baseUrl}/confirm-email?token={Uri.EscapeDataString(token)}";
        try
        {
            await _email.SendAsync(
                email,
                "Confirm your Misty account",
                $"<p>Welcome to Misty, {System.Net.WebUtility.HtmlEncode(cmd.DisplayName)}!</p>" +
                "<p>Click the button below to confirm your email address and activate your account.</p>" +
                $"<p><a href=\"{confirmUrl}\" style=\"background:#6c5ce7;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;\">Confirm email</a></p>" +
                $"<p>Or paste this link in your browser:<br><a href=\"{confirmUrl}\">{confirmUrl}</a></p>" +
                "<p>If you didn't create an account, you can ignore this email.</p>",
                ct);
        }
        catch
        {
            // Email sending is non-fatal. Account is created but email may not arrive in local dev.
        }

        return new RegisterUserResponse(user.Id, confirmUrl);
    }
}
