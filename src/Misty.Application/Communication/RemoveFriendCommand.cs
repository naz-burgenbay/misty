using FluentValidation;
using MediatR;

namespace Misty.Application.Communication;

public record RemoveFriendCommand(Guid UserId, Guid OtherUserId) : IRequest;

public sealed class RemoveFriendValidator : AbstractValidator<RemoveFriendCommand>
{
    public RemoveFriendValidator()
    {
        RuleFor(x => x.OtherUserId).NotEqual(x => x.UserId);
    }
}

public sealed class RemoveFriendCommandHandler : IRequestHandler<RemoveFriendCommand>
{
    private readonly IFriendshipRepository _friendships;

    public RemoveFriendCommandHandler(IFriendshipRepository friendships) => _friendships = friendships;

    public Task Handle(RemoveFriendCommand cmd, CancellationToken ct)
        => _friendships.DeleteForPairAsync(cmd.UserId, cmd.OtherUserId, ct);
}
