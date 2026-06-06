using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record LeaveChannelCommand(Guid UserId, Guid ChannelId) : IRequest;

public sealed class LeaveChannelCommandHandler : IRequestHandler<LeaveChannelCommand>
{
    private readonly IChannelRepository _channels;
    private readonly IMembershipRepository _memberships;
    private readonly IOutboxWriter _outbox;

    public LeaveChannelCommandHandler(
        IChannelRepository channels,
        IMembershipRepository memberships,
        IOutboxWriter outbox)
    {
        _channels = channels;
        _memberships = memberships;
        _outbox = outbox;
    }

    public async Task Handle(LeaveChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.UserId, ct)
            ?? throw new NotFoundException("User is not a member of this channel.");

        await _memberships.RemoveAsync(membership, channel, ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Membership, PermissionEventTypes.MembershipLeft, request.ChannelId,
            new MembershipLeftPayload(membership.Id, request.ChannelId, request.UserId, DateTime.UtcNow), ct);
    }
}
