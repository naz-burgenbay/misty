using MediatR;

namespace Misty.Application.Users;

public record DeleteUserCommand(Guid UserId) : IRequest;

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly IUserRepository _users;

    public DeleteUserCommandHandler(IUserRepository users) => _users = users;

    public async Task Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return;

        await _users.SoftDeleteAsync(user, ct);
    }
}
