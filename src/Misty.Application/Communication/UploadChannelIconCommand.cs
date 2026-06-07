using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record UploadChannelIconCommand(Guid ChannelId, Guid UserId, Stream Content, string ContentType, string Version)
    : IRequest<UploadChannelIconResponse>;

public record UploadChannelIconResponse(string IconUrl, string Version);

public sealed class UploadChannelIconCommandValidator : AbstractValidator<UploadChannelIconCommand>
{
    public UploadChannelIconCommandValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Version).NotEmpty();
    }
}

public sealed class UploadChannelIconCommandHandler : IRequestHandler<UploadChannelIconCommand, UploadChannelIconResponse>
{
    private readonly IChannelIconService _icons;
    private readonly IChannelRepository _channels;
    private readonly IPermissionService _permissions;

    public UploadChannelIconCommandHandler(
        IChannelIconService icons,
        IChannelRepository channels,
        IPermissionService permissions)
    {
        _icons = icons;
        _channels = channels;
        _permissions = permissions;
    }

    public async Task<UploadChannelIconResponse> Handle(UploadChannelIconCommand request, CancellationToken ct)
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

        var concurrencyToken = Convert.FromBase64String(request.Version);
        var url = await _icons.UploadAsync(request.ChannelId, request.Content, request.ContentType, ct);
        await _channels.UpdateIconUrlAsync(channel, url, concurrencyToken, ct);

        return new UploadChannelIconResponse(url, Convert.ToBase64String(channel.Version));
    }
}
