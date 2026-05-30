using FluentValidation;
using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record UpdateChannelCommand(
    Guid ChannelId,
    string Name,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions,
    string Version)
    : IRequest<UpdateChannelResponse>;

public record UpdateChannelResponse(
    Guid ChannelId,
    string Name,
    bool IsPrivate,
    string? InviteCode,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    int MemberCount,
    DateTime? LastMessageAt,
    string Version);

public sealed class UpdateChannelCommandHandler : IRequestHandler<UpdateChannelCommand, UpdateChannelResponse>
{
    private readonly IChannelRepository _channels;

    public UpdateChannelCommandHandler(IChannelRepository channels) => _channels = channels;

    public async Task<UpdateChannelResponse> Handle(UpdateChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        byte[] concurrencyToken;
        try { concurrencyToken = Convert.FromBase64String(request.Version); }
        catch (FormatException)
        {
            throw new ValidationException(
                [new("Version", "Invalid version token.")]);
        }

        channel.Update(request.Name, request.IsAiAssistantEnabled, request.DefaultPermissions, channel.Description);
        await _channels.UpdateAsync(channel, concurrencyToken, ct);

        return new UpdateChannelResponse(
            channel.Id,
            channel.Name,
            channel.IsPrivate,
            channel.InviteCode,
            channel.IsAiAssistantEnabled,
            (long)channel.DefaultPermissions,
            channel.MemberCount,
            channel.LastMessageAt,
            Convert.ToBase64String(channel.Version));
    }
}

public sealed class UpdateChannelValidator : AbstractValidator<UpdateChannelCommand>
{
    public UpdateChannelValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Version).NotEmpty();
    }
}
