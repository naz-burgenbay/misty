using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AcceptChannelInviteCommand(Guid UserId, Guid InviteId) : IRequest;

public sealed class AcceptChannelInviteCommandHandler : IRequestHandler<AcceptChannelInviteCommand>
{
    private readonly IChannelInviteRepository _invites;
    private readonly IChannelRepository _channels;
    private readonly IOutboxWriter _outbox;
    private readonly IMediator _mediator;

    public AcceptChannelInviteCommandHandler(
        IChannelInviteRepository invites,
        IChannelRepository channels,
        IOutboxWriter outbox,
        IMediator mediator)
    {
        _invites = invites;
        _channels = channels;
        _outbox = outbox;
        _mediator = mediator;
    }

    public async Task Handle(AcceptChannelInviteCommand cmd, CancellationToken ct)
    {
        var entity = await _invites.GetByIdAsync(cmd.InviteId, ct)
            ?? throw new NotFoundException("Channel invite not found.");

        if (entity.InvitedUserId != cmd.UserId)
            throw new ForbiddenException("Only the invited user can accept this invite.");

        if (entity.Status != ChannelInviteStatus.Pending)
            throw new ConflictException("Channel invite is no longer pending.");

        var channel = await _channels.GetByIdAsync(entity.ChannelId, ct)
            ?? throw new NotFoundException("Channel no longer exists.");

        entity.Accept();

        _outbox.Queue(
            SocialEventTopics.ChannelInvite,
            SocialEventTypes.ChannelInviteAccepted,
            entity.Id,
            new ChannelInviteAcceptedPayload(entity.Id, entity.ChannelId, cmd.UserId, entity.InvitedByUserId, DateTime.UtcNow));

        await _invites.UpdateAsync(entity, ct);

        // Delegate the actual join (membership row, default role assignment, MembershipChanged event) to the existing join handler.
        // Pass the channel's invite code so the private-channel gate accepts the join; the invite itself is the authorization.
        await _mediator.Send(new JoinChannelCommand(cmd.UserId, entity.ChannelId, channel.InviteCode), ct);
    }
}
