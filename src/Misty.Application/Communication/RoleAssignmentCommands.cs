using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AssignRoleCommand(Guid ChannelId, Guid ActorUserId, Guid TargetUserId, Guid RoleId) : IRequest;

public sealed class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand>
{
    private readonly IChannelRoleRepository _roles;
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public AssignRoleCommandHandler(
        IChannelRoleRepository roles,
        IMembershipRepository memberships,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _roles = roles;
        _memberships = memberships;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task Handle(AssignRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageRoles, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageRoles permission.");

        if (role.IsOwnerRole)
            throw new ForbiddenException("The Owner role cannot be assigned.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("Target user is not a member of this channel.");

        var existing = await _memberships.GetRoleAssignmentAsync(membership.Id, request.RoleId, ct);
        if (existing is not null)
            throw new ConflictException("User already has this role.");

        await _memberships.AssignRoleAsync(MemberRole.Create(membership.Id, request.RoleId), ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Role, PermissionEventTypes.MemberRoleAssigned, request.ChannelId,
            new MemberRoleAssignedPayload(
                request.ChannelId,
                request.TargetUserId,
                request.RoleId,
                request.ActorUserId,
                DateTime.UtcNow),
            ct);
    }
}

public record RevokeRoleCommand(Guid ChannelId, Guid ActorUserId, Guid TargetUserId, Guid RoleId) : IRequest;

public sealed class RevokeRoleCommandHandler : IRequestHandler<RevokeRoleCommand>
{
    private readonly IChannelRepository _channels;
    private readonly IChannelRoleRepository _roles;
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public RevokeRoleCommandHandler(
        IChannelRepository channels,
        IChannelRoleRepository roles,
        IMembershipRepository memberships,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _channels = channels;
        _roles = roles;
        _memberships = memberships;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task Handle(RevokeRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageRoles, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageRoles permission.");

        if (role.IsOwnerRole)
        {
            var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
                ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");
            if (channel.CreatedByUserId == request.TargetUserId)
                throw new ForbiddenException("The Owner role cannot be revoked from the channel creator.");
        }

        var membership = await _memberships.GetAsync(request.ChannelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("Target user is not a member of this channel.");

        var assignment = await _memberships.GetRoleAssignmentAsync(membership.Id, request.RoleId, ct)
            ?? throw new NotFoundException("User does not have this role.");

        await _memberships.RevokeRoleAsync(assignment, ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Role, PermissionEventTypes.MemberRoleRevoked, request.ChannelId,
            new MemberRoleRevokedPayload(
                request.ChannelId,
                request.TargetUserId,
                request.RoleId,
                request.ActorUserId,
                DateTime.UtcNow),
            ct);
    }
}
