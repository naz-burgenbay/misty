using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record AssignRoleCommand(Guid ChannelId, Guid TargetUserId, Guid RoleId) : IRequest;

public sealed class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand>
{
    private readonly IChannelRoleRepository _roles;
    private readonly IMembershipRepository _memberships;

    public AssignRoleCommandHandler(IChannelRoleRepository roles, IMembershipRepository memberships)
    {
        _roles = roles;
        _memberships = memberships;
    }

    public async Task Handle(AssignRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("Target user is not a member of this channel.");

        var existing = await _memberships.GetRoleAssignmentAsync(membership.Id, request.RoleId, ct);
        if (existing is not null)
            throw new ConflictException("User already has this role.");

        await _memberships.AssignRoleAsync(MemberRole.Create(membership.Id, request.RoleId), ct);
    }
}

public record RevokeRoleCommand(Guid ChannelId, Guid TargetUserId, Guid RoleId) : IRequest;

public sealed class RevokeRoleCommandHandler : IRequestHandler<RevokeRoleCommand>
{
    private readonly IChannelRoleRepository _roles;
    private readonly IMembershipRepository _memberships;

    public RevokeRoleCommandHandler(IChannelRoleRepository roles, IMembershipRepository memberships)
    {
        _roles = roles;
        _memberships = memberships;
    }

    public async Task Handle(RevokeRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var membership = await _memberships.GetAsync(request.ChannelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("Target user is not a member of this channel.");

        var assignment = await _memberships.GetRoleAssignmentAsync(membership.Id, request.RoleId, ct)
            ?? throw new NotFoundException("User does not have this role.");

        await _memberships.RevokeRoleAsync(assignment, ct);
    }
}
