using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record LeaveChannelCommand(Guid UserId, Guid ChannelId) : IRequest;

public sealed class LeaveChannelCommandHandler : IRequestHandler<LeaveChannelCommand>
{
    private readonly IChannelRepository _channels;
    private readonly IMembershipRepository _memberships;
    private readonly IEventPublisher _events;

    public LeaveChannelCommandHandler(
        IChannelRepository channels,
        IMembershipRepository memberships,
        IEventPublisher events)
    {
        _channels = channels;
        _memberships = memberships;
        _events = events;
    }

    public async Task Handle(LeaveChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.UserId, ct)
            ?? throw new NotFoundException("User is not a member of this channel.");

        await _memberships.RemoveAsync(membership, channel, ct);
        await _events.PublishMembershipChangedAsync(request.UserId, request.ChannelId, ct);
    }
}
