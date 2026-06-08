using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AcceptChannelInviteCommand(Guid UserId, Guid InviteId, string Version) : IRequest;

public sealed class AcceptChannelInviteCommandValidator : AbstractValidator<AcceptChannelInviteCommand>
{
    public AcceptChannelInviteCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.InviteId).NotEmpty();
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class AcceptChannelInviteCommandHandler : IRequestHandler<AcceptChannelInviteCommand>
{
    private readonly IChannelInviteRepository _invites;
    private readonly IChannelRepository _channels;
    private readonly IOutboxWriter _outbox;
    private readonly IMediator _mediator;
    private readonly IInboxItemRepository _inbox;

    public AcceptChannelInviteCommandHandler(
        IChannelInviteRepository invites,
        IChannelRepository channels,
        IOutboxWriter outbox,
        IMediator mediator,
        IInboxItemRepository inbox)
    {
        _invites = invites;
        _channels = channels;
        _outbox = outbox;
        _mediator = mediator;
        _inbox = inbox;
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

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(cmd.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        _outbox.Queue(
            SocialEventTopics.ChannelInvite,
            SocialEventTypes.ChannelInviteAccepted,
            entity.Id,
            new ChannelInviteAcceptedPayload(entity.Id, entity.ChannelId, cmd.UserId, entity.InvitedByUserId, DateTime.UtcNow));

        await _invites.UpdateAsync(entity, concurrencyToken, ct);

        var inboxItem = await _inbox.GetByReferenceAsync(cmd.UserId, cmd.InviteId, ct);
        if (inboxItem is { IsActedOn: false })
        {
            inboxItem.MarkActedOn();
            await _inbox.UpdateAsync(inboxItem, ct);
        }

        await _mediator.Send(new JoinChannelCommand(cmd.UserId, entity.ChannelId, channel.InviteCode), ct);
    }
}
