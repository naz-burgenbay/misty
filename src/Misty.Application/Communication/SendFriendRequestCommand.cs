using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record SendFriendRequestCommand(Guid SenderId, string Username) : IRequest<FriendRequestDto>;

public sealed class SendFriendRequestValidator : AbstractValidator<SendFriendRequestCommand>
{
    public SendFriendRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
    }
}

public sealed class SendFriendRequestCommandHandler : IRequestHandler<SendFriendRequestCommand, FriendRequestDto>
{
    private readonly IUserRepository _users;
    private readonly IFriendRequestRepository _requests;
    private readonly IFriendshipRepository _friendships;
    private readonly IUserBlockService _blocks;
    private readonly IOutboxWriter _outbox;

    public SendFriendRequestCommandHandler(
        IUserRepository users,
        IFriendRequestRepository requests,
        IFriendshipRepository friendships,
        IUserBlockService blocks,
        IOutboxWriter outbox)
    {
        _users = users;
        _requests = requests;
        _friendships = friendships;
        _blocks = blocks;
        _outbox = outbox;
    }

    public async Task<FriendRequestDto> Handle(SendFriendRequestCommand request, CancellationToken ct)
    {
        var sender = await _users.GetByIdAsync(request.SenderId, ct)
            ?? throw new NotFoundException("Sender not found.");

        var receiver = await _users.GetByUsernameAsync(request.Username, ct)
            ?? throw new NotFoundException($"User '{request.Username}' not found.");

        if (receiver.Id == sender.Id)
            throw new ValidationException("You cannot send a friend request to yourself.");

        if (await _blocks.IsBlockedAsync(sender.Id, receiver.Id, ct))
            throw new ForbiddenException("Cannot send a friend request to this user.");

        if (await _friendships.ExistsAsync(sender.Id, receiver.Id, ct))
            throw new ConflictException("You are already friends with this user.");

        var existing = await _requests.GetPendingBetweenAsync(sender.Id, receiver.Id, ct);
        if (existing is not null)
            throw new ConflictException("A pending friend request already exists between you and this user.");

        var entity = FriendRequest.Create(Guid.NewGuid(), sender.Id, receiver.Id);

        _outbox.Queue(
            SocialEventTopics.Friend,
            SocialEventTypes.FriendRequestSent,
            entity.Id,
            new FriendRequestSentPayload(entity.Id, sender.Id, receiver.Id, DateTime.UtcNow));

        await _requests.AddAsync(entity, ct);

        return new FriendRequestDto(
            entity.Id,
            sender.Id,
            sender.Username,
            sender.DisplayName,
            sender.AvatarUrl,
            entity.Status.ToString(),
            entity.CreatedAt,
            entity.RespondedAt);
    }
}
