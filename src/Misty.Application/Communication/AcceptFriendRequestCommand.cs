using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AcceptFriendRequestCommand(Guid AccepterId, Guid RequestId) : IRequest<FriendDto>;

public sealed class AcceptFriendRequestCommandValidator : AbstractValidator<AcceptFriendRequestCommand>
{
    public AcceptFriendRequestCommandValidator()
    {
        RuleFor(x => x.AccepterId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty();
    }
}

public sealed class AcceptFriendRequestCommandHandler : IRequestHandler<AcceptFriendRequestCommand, FriendDto>
{
    private readonly IFriendRequestRepository _requests;
    private readonly IFriendshipRepository _friendships;
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;
    private readonly IInboxItemRepository _inbox;

    public AcceptFriendRequestCommandHandler(
        IFriendRequestRepository requests,
        IFriendshipRepository friendships,
        IUserRepository users,
        IOutboxWriter outbox,
        IInboxItemRepository inbox)
    {
        _requests = requests;
        _friendships = friendships;
        _users = users;
        _outbox = outbox;
        _inbox = inbox;
    }

    public async Task<FriendDto> Handle(AcceptFriendRequestCommand cmd, CancellationToken ct)
    {
        var entity = await _requests.GetByIdAsync(cmd.RequestId, ct)
            ?? throw new NotFoundException("Friend request not found.");

        if (entity.ReceiverId != cmd.AccepterId)
            throw new ForbiddenException("Only the receiver can accept this friend request.");

        if (entity.Status != FriendRequestStatus.Pending)
            throw new ConflictException("Friend request is no longer pending.");

        entity.Accept();

        var friendship = Friendship.Create(Guid.NewGuid(), entity.SenderId, entity.ReceiverId);

        _outbox.Queue(
            SocialEventTopics.Friend,
            SocialEventTypes.FriendRequestAccepted,
            entity.Id,
            new FriendRequestAcceptedPayload(entity.Id, cmd.AccepterId, entity.SenderId, DateTime.UtcNow));

        _outbox.Queue(
            SocialEventTopics.Friend,
            SocialEventTypes.FriendshipCreated,
            friendship.Id,
            new FriendshipCreatedPayload(
                friendship.Id,
                friendship.UserAId,
                friendship.UserBId,
                cmd.AccepterId,
                DateTime.UtcNow));

        var inboxItem = await _inbox.GetByReferenceAsync(cmd.AccepterId, cmd.RequestId, ct);
        if (inboxItem is { IsActedOn: false })
        {
            inboxItem.MarkActedOn();
            await _inbox.UpdateAsync(inboxItem, ct);
        }

        await _friendships.AddAsync(friendship, ct);

        var senderUser = await _users.GetByIdAsync(entity.SenderId, ct)
            ?? throw new NotFoundException("Sender not found.");

        return new FriendDto(senderUser.Id, senderUser.Username, senderUser.DisplayName, senderUser.AvatarUrl, Convert.ToBase64String(friendship.Version));
    }
}
