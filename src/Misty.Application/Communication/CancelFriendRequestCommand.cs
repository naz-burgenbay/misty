using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record CancelFriendRequestCommand(Guid UserId, Guid RequestId) : IRequest;

public sealed class CancelFriendRequestCommandHandler : IRequestHandler<CancelFriendRequestCommand>
{
    private readonly IFriendRequestRepository _requests;

    public CancelFriendRequestCommandHandler(IFriendRequestRepository requests) => _requests = requests;

    public async Task Handle(CancelFriendRequestCommand cmd, CancellationToken ct)
    {
        var entity = await _requests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("Friend request not found.");

        if (entity.SenderId != cmd.UserId)
            throw new ForbiddenException("Only the sender can cancel this friend request.");

        if (entity.Status != FriendRequestStatus.Pending)
            throw new ConflictException("Friend request is no longer pending.");

        entity.Decline();
        await _requests.UpdateAsync(entity, ct);
    }
}
