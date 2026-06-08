using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Users;

public record DeleteUserCommand(Guid UserId) : IRequest;

public sealed class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;

    public DeleteUserCommandHandler(IUserRepository users, IOutboxWriter outbox)
    {
        _users = users;
        _outbox = outbox;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct);
        if (user is null)
            return;

        _outbox.Queue(
            UserEventTopics.User,
            UserEventTypes.UserDeleted,
            user.Id,
            new UserDeletedPayload(user.Id, DateTime.UtcNow));

        await _users.SoftDeleteAsync(user, ct);
    }
}
