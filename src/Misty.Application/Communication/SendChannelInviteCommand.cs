using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record SendChannelInviteCommand(Guid InviterId, Guid ChannelId, string Username) : IRequest<ChannelInviteDto>;

public sealed class SendChannelInviteValidator : AbstractValidator<SendChannelInviteCommand>
{
    public SendChannelInviteValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
    }
}

public sealed class SendChannelInviteCommandHandler : IRequestHandler<SendChannelInviteCommand, ChannelInviteDto>
{
    private readonly IPermissionService _permissions;
    private readonly IChannelRepository _channels;
    private readonly IMembershipRepository _memberships;
    private readonly IChannelInviteRepository _invites;
    private readonly IUserRepository _users;
    private readonly IUserBlockService _blocks;
    private readonly IOutboxWriter _outbox;

    public SendChannelInviteCommandHandler(
        IPermissionService permissions,
        IChannelRepository channels,
        IMembershipRepository memberships,
        IChannelInviteRepository invites,
        IUserRepository users,
        IUserBlockService blocks,
        IOutboxWriter outbox)
    {
        _permissions = permissions;
        _channels = channels;
        _memberships = memberships;
        _invites = invites;
        _users = users;
        _blocks = blocks;
        _outbox = outbox;
    }

    public async Task<ChannelInviteDto> Handle(SendChannelInviteCommand cmd, CancellationToken ct)
    {
        var canInvite = await _permissions.CheckPermissionAsync(
            cmd.InviterId, cmd.ChannelId, ChannelPermission.InviteMembers, ct);
        if (!canInvite)
            throw new ForbiddenException("You do not have permission to invite members to this channel.");

        var channel = await _channels.GetByIdAsync(cmd.ChannelId, ct)
            ?? throw new NotFoundException("Channel not found.");

        var inviter = await _users.GetByIdAsync(cmd.InviterId, ct)
            ?? throw new NotFoundException("Inviter not found.");

        var invitee = await _users.GetByUsernameAsync(cmd.Username, ct)
            ?? throw new NotFoundException($"User '{cmd.Username}' not found.");

        if (invitee.Id == inviter.Id)
            throw new ValidationException("You cannot invite yourself.");

        if (await _blocks.IsBlockedAsync(inviter.Id, invitee.Id, ct))
            throw new ForbiddenException("Cannot invite this user.");

        var existingMembership = await _memberships.GetAsync(cmd.ChannelId, invitee.Id, ct);
        if (existingMembership is not null)
            throw new ConflictException("User is already a member of this channel.");

        var existingInvite = await _invites.GetPendingAsync(cmd.ChannelId, invitee.Id, ct);
        if (existingInvite is not null)
            throw new ConflictException("A pending invite already exists for this user.");

        var entity = ChannelInvite.Create(Guid.NewGuid(), cmd.ChannelId, inviter.Id, invitee.Id);

        _outbox.Queue(
            SocialEventTopics.ChannelInvite,
            SocialEventTypes.ChannelInviteSent,
            entity.Id,
            new ChannelInviteSentPayload(entity.Id, channel.Id, inviter.Id, invitee.Id, DateTime.UtcNow));

        await _invites.AddAsync(entity, ct);

        return new ChannelInviteDto(
            entity.Id,
            channel.Id,
            channel.Name,
            inviter.Id,
            inviter.DisplayName,
            entity.Status.ToString(),
            entity.CreatedAt,
            Convert.ToBase64String(entity.Version));
    }
}
