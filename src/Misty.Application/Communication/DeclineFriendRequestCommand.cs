using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record DeclineFriendRequestCommand(Guid UserId, Guid RequestId, string Version) : IRequest;

public sealed class DeclineFriendRequestCommandValidator : AbstractValidator<DeclineFriendRequestCommand>
{
    public DeclineFriendRequestCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class DeclineFriendRequestCommandHandler : IRequestHandler<DeclineFriendRequestCommand>
{
    private readonly IFriendRequestRepository _requests;
    private readonly IOutboxWriter _outbox;

    public DeclineFriendRequestCommandHandler(IFriendRequestRepository requests, IOutboxWriter outbox)
    {
        _requests = requests;
        _outbox = outbox;
    }

    public async Task Handle(DeclineFriendRequestCommand cmd, CancellationToken ct)
    {
        var entity = await _requests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("Friend request not found.");

        if (entity.ReceiverId != cmd.UserId)
            throw new ForbiddenException("Only the receiver can decline this friend request.");

        if (entity.Status != FriendRequestStatus.Pending)
            throw new ConflictException("Friend request is no longer pending.");

        entity.Decline();

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(cmd.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        _outbox.Queue(
            SocialEventTopics.Friend,
            SocialEventTypes.FriendRequestDeclined,
            entity.Id,
            new FriendRequestDeclinedPayload(entity.Id, cmd.UserId, entity.SenderId, DateTime.UtcNow));

        await _requests.UpdateAsync(entity, concurrencyToken, ct);
    }
}
