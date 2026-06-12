using MediatR;
using Misty.Application.Common.Exceptions;

namespace Misty.Application.Users;

public record ConfirmEmailCommand(string Token) : IRequest;

public sealed class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand>
{
    private readonly IUserRepository _users;

    public ConfirmEmailCommandHandler(IUserRepository users) => _users = users;

    public async Task Handle(ConfirmEmailCommand request, CancellationToken ct)
    {
        var user = await _users.GetByConfirmationTokenAsync(request.Token, ct)
            ?? throw new NotFoundException("Invalid or expired confirmation token.");

        if (!user.ConfirmEmail(request.Token))
            throw new ConflictException("Email is already confirmed.");

        await _users.SaveChangesAsync(ct);
    }
}
