using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record KickMemberCommand(
    Guid ChannelId,
    Guid TargetUserId,
    Guid IssuedByUserId,
    string Reason)
    : IRequest<KickMemberResponse>;

public record KickMemberResponse(Guid ActionId);

public sealed class KickMemberCommandHandler : IRequestHandler<KickMemberCommand, KickMemberResponse>
{
    private readonly IChannelRepository _channels;
    private readonly IMembershipRepository _memberships;
    private readonly IModerationRepository _moderation;
    private readonly IPermissionService _permissions;
    private readonly IEventPublisher _events;

    public KickMemberCommandHandler(
        IChannelRepository channels,
        IMembershipRepository memberships,
        IModerationRepository moderation,
        IPermissionService permissions,
        IEventPublisher events)
    {
        _channels = channels;
        _memberships = memberships;
        _moderation = moderation;
        _permissions = permissions;
        _events = events;
    }

    public async Task<KickMemberResponse> Handle(KickMemberCommand request, CancellationToken ct)
    {
        if (request.IssuedByUserId == request.TargetUserId)
            throw new ForbiddenException("Cannot kick yourself.");

        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        if (channel.CreatedByUserId == request.TargetUserId)
            throw new ForbiddenException("Cannot kick the channel owner.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.IssuedByUserId, request.ChannelId, ChannelPermission.KickMembers, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing KickMembers permission.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("Target user is not a member of this channel.");

        await _memberships.SoftRemoveAsync(membership, channel, ct);

        var action = ModerationAction.Create(
            Guid.NewGuid(),
            request.ChannelId,
            request.TargetUserId,
            request.IssuedByUserId,
            ModerationActionType.Kick,
            request.Reason.Trim(),
            expiresAt: null);

        await _moderation.AddAsync(action, ct);
        await _events.PublishMembershipChangedAsync(request.TargetUserId, request.ChannelId, ct);

        return new KickMemberResponse(action.Id);
    }
}

public sealed class KickMemberValidator : AbstractValidator<KickMemberCommand>
{
    public KickMemberValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.IssuedByUserId).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(512);
    }
}
