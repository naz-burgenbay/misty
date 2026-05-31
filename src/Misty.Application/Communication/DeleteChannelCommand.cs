using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record DeleteChannelCommand(Guid ChannelId, Guid ActorUserId) : IRequest;

public sealed class DeleteChannelCommandHandler : IRequestHandler<DeleteChannelCommand>
{
    private readonly IChannelRepository _channels;
    private readonly IPermissionService _permissions;

    public DeleteChannelCommandHandler(IChannelRepository channels, IPermissionService permissions)
    {
        _channels = channels;
        _permissions = permissions;
    }

    public async Task Handle(DeleteChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        if (channel.CreatedByUserId != request.ActorUserId)
            throw new ForbiddenException("Only the channel owner can delete the channel.");

        var hasPermission = await _permissions.CheckPermissionAsync(
            request.ActorUserId, request.ChannelId, ChannelPermission.ManageChannel, ct);
        if (!hasPermission)
            throw new ForbiddenException("Missing ManageChannel permission.");

        await _channels.SoftDeleteAsync(channel, ct);
    }
}
