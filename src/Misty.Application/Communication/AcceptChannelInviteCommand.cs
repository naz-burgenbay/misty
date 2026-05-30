using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AcceptChannelInviteCommand(Guid UserId, Guid InviteId) : IRequest;

public sealed class AcceptChannelInviteCommandHandler : IRequestHandler<AcceptChannelInviteCommand>
{
    private readonly IChannelInviteRepository _invites;
    private readonly IMediator _mediator;

    public AcceptChannelInviteCommandHandler(IChannelInviteRepository invites, IMediator mediator)
    {
        _invites = invites;
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

        entity.Accept();
        await _invites.UpdateAsync(entity, ct);

        // Delegate the actual join (membership row, default role assignment, MembershipChanged event) to the existing join handler.
        // This handler must not insert a Membership row directly.
        await _mediator.Send(new JoinChannelCommand(cmd.UserId, entity.ChannelId, InviteCode: null), ct);
    }
}
