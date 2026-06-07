using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record DeclineChannelInviteCommand(Guid UserId, Guid InviteId, string Version) : IRequest;

public sealed class DeclineChannelInviteCommandValidator : AbstractValidator<DeclineChannelInviteCommand>
{
    public DeclineChannelInviteCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.InviteId).NotEmpty();
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class DeclineChannelInviteCommandHandler : IRequestHandler<DeclineChannelInviteCommand>
{
    private readonly IChannelInviteRepository _invites;
    private readonly IOutboxWriter _outbox;

    public DeclineChannelInviteCommandHandler(IChannelInviteRepository invites, IOutboxWriter outbox)
    {
        _invites = invites;
        _outbox = outbox;
    }

    public async Task Handle(DeclineChannelInviteCommand cmd, CancellationToken ct)
    {
        var entity = await _invites.GetByIdAsync(cmd.InviteId, ct)
            ?? throw new NotFoundException("Channel invite not found.");

        if (entity.InvitedUserId != cmd.UserId)
            throw new ForbiddenException("Only the invited user can decline this invite.");

        if (entity.Status != ChannelInviteStatus.Pending)
            throw new ConflictException("Channel invite is no longer pending.");

        entity.Decline();

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(cmd.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        _outbox.Queue(
            SocialEventTopics.ChannelInvite,
            SocialEventTypes.ChannelInviteDeclined,
            entity.Id,
            new ChannelInviteDeclinedPayload(entity.Id, entity.ChannelId, cmd.UserId, entity.InvitedByUserId, DateTime.UtcNow));

        await _invites.UpdateAsync(entity, concurrencyToken, ct);
    }
}
