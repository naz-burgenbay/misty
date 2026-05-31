using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record RemoveChannelIconCommand(Guid ChannelId, Guid UserId) : IRequest<RemoveChannelIconResponse>;

public record RemoveChannelIconResponse(string Version);

public sealed class RemoveChannelIconCommandHandler : IRequestHandler<RemoveChannelIconCommand, RemoveChannelIconResponse>
{
    private readonly IChannelIconService _icons;
    private readonly IChannelRepository _channels;
    private readonly IPermissionService _permissions;

    public RemoveChannelIconCommandHandler(
        IChannelIconService icons,
        IChannelRepository channels,
        IPermissionService permissions)
    {
        _icons = icons;
        _channels = channels;
        _permissions = permissions;
    }

    public async Task<RemoveChannelIconResponse> Handle(RemoveChannelIconCommand request, CancellationToken ct)
    {
        var canManage = await _permissions.CheckPermissionAsync(
            request.UserId,
            request.ChannelId,
            ChannelPermission.ManageChannel,
            ct);

        if (!canManage)
            throw new ForbiddenException("You do not have permission to manage this channel.");

        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException("Channel not found.");

        await _icons.DeleteAsync(request.ChannelId, ct);
        await _channels.UpdateIconUrlAsync(channel, null, ct);

        return new RemoveChannelIconResponse(Convert.ToBase64String(channel.Version));
    }
}
