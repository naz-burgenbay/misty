using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record JoinChannelCommand(Guid UserId, Guid ChannelId, string? InviteCode) : IRequest<JoinChannelResponse>;

public record JoinChannelResponse(Guid MembershipId, Guid ChannelId, DateTime JoinedAt);

public sealed class JoinChannelCommandHandler : IRequestHandler<JoinChannelCommand, JoinChannelResponse>
{
    private readonly IChannelRepository _channels;
    private readonly IMembershipRepository _memberships;
    private readonly IEventPublisher _events;

    public JoinChannelCommandHandler(
        IChannelRepository channels,
        IMembershipRepository memberships,
        IEventPublisher events)
    {
        _channels = channels;
        _memberships = memberships;
        _events = events;
    }

    public async Task<JoinChannelResponse> Handle(JoinChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        if (channel.IsPrivate)
        {
            if (string.IsNullOrWhiteSpace(request.InviteCode) ||
                !string.Equals(request.InviteCode, channel.InviteCode, StringComparison.Ordinal))
                throw new ForbiddenException("Invalid or missing invite code.");
        }

        var existing = await _memberships.GetAsync(request.ChannelId, request.UserId, ct);
        if (existing is not null)
            throw new ConflictException("User is already a member of this channel.");

        var membership = Membership.Create(Guid.NewGuid(), request.ChannelId, request.UserId);
        await _memberships.AddAsync(membership, channel, ct);
        await _events.PublishMembershipChangedAsync(request.UserId, request.ChannelId, ct);

        return new JoinChannelResponse(membership.Id, membership.ChannelId, membership.JoinedAt);
    }
}
