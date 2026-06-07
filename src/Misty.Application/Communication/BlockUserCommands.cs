using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record BlockUserCommand(Guid BlockerId, Guid BlockedId) : IRequest;

public sealed class BlockUserValidator : AbstractValidator<BlockUserCommand>
{
    public BlockUserValidator()
    {
        RuleFor(x => x.BlockedId)
            .NotEqual(x => x.BlockerId)
            .WithMessage("Cannot block yourself.");
    }
}

public sealed class BlockUserCommandHandler : IRequestHandler<BlockUserCommand>
{
    private readonly IUserBlockService _svc;
    private readonly IFriendshipRepository _friendships;
    private readonly IOutboxWriter _outbox;

    public BlockUserCommandHandler(
        IUserBlockService svc,
        IFriendshipRepository friendships,
        IOutboxWriter outbox)
    {
        _svc = svc;
        _friendships = friendships;
        _outbox = outbox;
    }

    public async Task Handle(BlockUserCommand request, CancellationToken ct)
    {
        // Resolve the friendship row (if any) before blocking so we have its Id for the FriendshipRemoved payload.
        // Block cascades a hard-delete of the friendship via UserBlockService; lifecycle completeness requires that cascade emit FriendshipRemoved.
        var friendship = await _friendships.GetForPairAsync(request.BlockerId, request.BlockedId, ct);

        var created = await _svc.BlockAsync(request.BlockerId, request.BlockedId, ct);
        if (!created)
            return;

        await _outbox.WriteAsync(
            BlockEventTopics.Block,
            BlockEventTypes.UserBlocked,
            request.BlockerId,
            new UserBlockedPayload(request.BlockerId, request.BlockedId, DateTime.UtcNow),
            ct);

        if (friendship is not null)
        {
            await _outbox.WriteAsync(
                SocialEventTopics.Friend,
                SocialEventTypes.FriendshipRemoved,
                friendship.Id,
                new FriendshipRemovedPayload(
                    friendship.Id,
                    friendship.UserAId,
                    friendship.UserBId,
                    request.BlockerId,
                    DateTime.UtcNow),
                ct);
        }
    }
}

public record UnblockUserCommand(Guid BlockerId, Guid BlockedId) : IRequest;

public sealed class UnblockUserCommandValidator : AbstractValidator<UnblockUserCommand>
{
    public UnblockUserCommandValidator()
    {
        RuleFor(x => x.BlockerId).NotEmpty();
        RuleFor(x => x.BlockedId).NotEmpty();
    }
}

public sealed class UnblockUserCommandHandler : IRequestHandler<UnblockUserCommand>
{
    private readonly IUserBlockService _svc;
    private readonly IOutboxWriter _outbox;

    public UnblockUserCommandHandler(IUserBlockService svc, IOutboxWriter outbox)
    {
        _svc = svc;
        _outbox = outbox;
    }

    public async Task Handle(UnblockUserCommand request, CancellationToken ct)
    {
        var removed = await _svc.UnblockAsync(request.BlockerId, request.BlockedId, ct);
        if (!removed)
            return;

        await _outbox.WriteAsync(
            BlockEventTopics.Block,
            BlockEventTypes.UserUnblocked,
            request.BlockerId,
            new UserUnblockedPayload(request.BlockerId, request.BlockedId, DateTime.UtcNow),
            ct);
    }
}
