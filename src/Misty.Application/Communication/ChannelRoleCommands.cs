using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record ChannelRoleResponse(Guid RoleId, Guid ChannelId, string Name, long Permissions, bool IsOwnerRole, string Version);

public record CreateChannelRoleCommand(Guid ChannelId, Guid ActorUserId, string Name, ChannelPermission Permissions)
    : IRequest<ChannelRoleResponse>;

public sealed class CreateChannelRoleCommandHandler : IRequestHandler<CreateChannelRoleCommand, ChannelRoleResponse>
{
    private readonly IChannelRepository _channels;
    private readonly IChannelRoleRepository _roles;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public CreateChannelRoleCommandHandler(
        IChannelRepository channels,
        IChannelRoleRepository roles,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _channels = channels;
        _roles = roles;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task<ChannelRoleResponse> Handle(CreateChannelRoleCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageRoles, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageRoles permission.");

        var role = ChannelRole.Create(Guid.NewGuid(), channel.Id, request.Name, request.Permissions);
        await _roles.AddAsync(role, ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Role, PermissionEventTypes.ChannelRoleCreated, request.ChannelId,
            new ChannelRoleCreatedPayload(request.ChannelId, role.Id, request.ActorUserId, DateTime.UtcNow),
            ct);

        return new ChannelRoleResponse(role.Id, role.ChannelId, role.Name, (long)role.Permissions, role.IsOwnerRole, Convert.ToBase64String(role.Version));
    }
}

public sealed class CreateChannelRoleValidator : AbstractValidator<CreateChannelRoleCommand>
{
    public CreateChannelRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public record UpdateChannelRoleCommand(Guid ChannelId, Guid ActorUserId, Guid RoleId, string Name, ChannelPermission Permissions, string Version)
    : IRequest<ChannelRoleResponse>;

public sealed class UpdateChannelRoleCommandHandler : IRequestHandler<UpdateChannelRoleCommand, ChannelRoleResponse>
{
    private readonly IChannelRoleRepository _roles;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public UpdateChannelRoleCommandHandler(
        IChannelRoleRepository roles,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _roles = roles;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task<ChannelRoleResponse> Handle(UpdateChannelRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageRoles, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageRoles permission.");

        if (role.IsOwnerRole)
            throw new ConflictException("The Owner role cannot be modified.");

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(request.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        role.Update(request.Name, request.Permissions);
        await _roles.UpdateAsync(role, concurrencyToken, ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Role, PermissionEventTypes.ChannelRoleUpdated, request.ChannelId,
            new ChannelRoleUpdatedPayload(request.ChannelId, role.Id, request.ActorUserId, DateTime.UtcNow),
            ct);

        return new ChannelRoleResponse(role.Id, role.ChannelId, role.Name, (long)role.Permissions, role.IsOwnerRole, Convert.ToBase64String(role.Version));
    }
}

public sealed class UpdateChannelRoleValidator : AbstractValidator<UpdateChannelRoleCommand>
{
    public UpdateChannelRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Version).NotEmpty();
    }
}

public record DeleteChannelRoleCommand(Guid ChannelId, Guid ActorUserId, Guid RoleId) : IRequest;

public sealed class DeleteChannelRoleCommandHandler : IRequestHandler<DeleteChannelRoleCommand>
{
    private readonly IChannelRoleRepository _roles;
    private readonly IPermissionService _permissions;
    private readonly IOutboxWriter _outbox;

    public DeleteChannelRoleCommandHandler(
        IChannelRoleRepository roles,
        IPermissionService permissions,
        IOutboxWriter outbox)
    {
        _roles = roles;
        _permissions = permissions;
        _outbox = outbox;
    }

    public async Task Handle(DeleteChannelRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null || role.ChannelId != request.ChannelId)
            throw new NotFoundException($"Role '{request.RoleId}' was not found in this channel.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageRoles, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageRoles permission.");

        if (role.IsOwnerRole)
            throw new ConflictException("The Owner role cannot be deleted.");

        await _roles.DeleteAsync(role, ct);
        await _outbox.WriteAsync(
            PermissionEventTopics.Role, PermissionEventTypes.ChannelRoleDeleted, request.ChannelId,
            new ChannelRoleDeletedPayload(request.ChannelId, role.Id, request.ActorUserId, DateTime.UtcNow),
            ct);
    }
}

public record GetChannelRolesQuery(Guid ChannelId) : IRequest<List<ChannelRoleResponse>>;

public sealed class GetChannelRolesQueryHandler : IRequestHandler<GetChannelRolesQuery, List<ChannelRoleResponse>>
{
    private readonly IChannelRepository _channels;
    private readonly IChannelRoleRepository _roles;

    public GetChannelRolesQueryHandler(IChannelRepository channels, IChannelRoleRepository roles)
    {
        _channels = channels;
        _roles = roles;
    }

    public async Task<List<ChannelRoleResponse>> Handle(GetChannelRolesQuery request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        var roles = await _roles.GetByChannelIdAsync(channel.Id, ct);
        return roles.Select(r => new ChannelRoleResponse(r.Id, r.ChannelId, r.Name, (long)r.Permissions, r.IsOwnerRole, Convert.ToBase64String(r.Version)))
                    .ToList();
    }
}
