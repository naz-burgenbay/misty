using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record DeclineFriendRequestCommand(Guid UserId, Guid RequestId) : IRequest;

public sealed class DeclineFriendRequestCommandHandler : IRequestHandler<DeclineFriendRequestCommand>
{
    private readonly IFriendRequestRepository _requests;

    public DeclineFriendRequestCommandHandler(IFriendRequestRepository requests) => _requests = requests;

    public async Task Handle(DeclineFriendRequestCommand cmd, CancellationToken ct)
    {
        var entity = await _requests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("Friend request not found.");

        if (entity.ReceiverId != cmd.UserId)
            throw new ForbiddenException("Only the receiver can decline this friend request.");

        if (entity.Status != FriendRequestStatus.Pending)
            throw new ConflictException("Friend request is no longer pending.");

        entity.Decline();
        await _requests.UpdateAsync(entity, ct);
    }
}
