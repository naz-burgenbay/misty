using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record DeclineChannelInviteCommand(Guid UserId, Guid InviteId) : IRequest;

public sealed class DeclineChannelInviteCommandHandler : IRequestHandler<DeclineChannelInviteCommand>
{
    private readonly IChannelInviteRepository _invites;

    public DeclineChannelInviteCommandHandler(IChannelInviteRepository invites) => _invites = invites;

    public async Task Handle(DeclineChannelInviteCommand cmd, CancellationToken ct)
    {
        var entity = await _invites.GetByIdAsync(cmd.InviteId, ct)
            ?? throw new NotFoundException("Channel invite not found.");

        if (entity.InvitedUserId != cmd.UserId)
            throw new ForbiddenException("Only the invited user can decline this invite.");

        if (entity.Status != ChannelInviteStatus.Pending)
            throw new ConflictException("Channel invite is no longer pending.");

        entity.Decline();
        await _invites.UpdateAsync(entity, ct);
    }
}
