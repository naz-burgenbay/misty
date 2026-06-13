using MediatR;

namespace Misty.Application.Users;

public record RegisterUserCommand(
    string Username,
    string Email,
    string DisplayName,
    string Password
) : IRequest<RegisterUserResponse>;

public record RegisterUserResponse(Guid UserId, string ConfirmationUrl);
