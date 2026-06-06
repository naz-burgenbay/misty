using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;

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
    private readonly IOutboxWriter _outbox;

    public RemoveFriendCommandHandler(IFriendshipRepository friendships, IOutboxWriter outbox)
    {
        _friendships = friendships;
        _outbox = outbox;
    }

    public async Task Handle(RemoveFriendCommand cmd, CancellationToken ct)
    {
        var friendship = await _friendships.GetForPairAsync(cmd.UserId, cmd.OtherUserId, ct);
        if (friendship is null)
            return;

        await _outbox.WriteAsync(
            SocialEventTopics.Friend,
            SocialEventTypes.FriendshipRemoved,
            friendship.Id,
            new FriendshipRemovedPayload(
                friendship.Id,
                friendship.UserAId,
                friendship.UserBId,
                cmd.UserId,
                DateTime.UtcNow),
            ct);

        await _friendships.DeleteForPairAsync(cmd.UserId, cmd.OtherUserId, ct);
    }
}
