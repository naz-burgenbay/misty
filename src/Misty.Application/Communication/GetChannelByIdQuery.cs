using FluentValidation;
using MediatR;

namespace Misty.Application.Communication;

public record GetChannelByIdQuery(Guid ChannelId) : IRequest<GetChannelByIdResponse?>;

public record GetChannelByIdResponse(
    Guid ChannelId,
    string Name,
    bool IsPrivate,
    string? InviteCode,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    int MemberCount,
    DateTime? LastMessageAt,
    string Version,
    string? IconUrl,
    string? Description);

public sealed class GetChannelByIdQueryValidator : AbstractValidator<GetChannelByIdQuery>
{
    public GetChannelByIdQueryValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
    }
}

public sealed class GetChannelByIdQueryHandler : IRequestHandler<GetChannelByIdQuery, GetChannelByIdResponse?>
{
    private readonly IChannelRepository _channels;

    public GetChannelByIdQueryHandler(IChannelRepository channels) => _channels = channels;

    public async Task<GetChannelByIdResponse?> Handle(GetChannelByIdQuery request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdForReadAsync(request.ChannelId, ct);
        if (channel is null) return null;

        return new GetChannelByIdResponse(
            channel.Id,
            channel.Name,
            channel.IsPrivate,
            channel.InviteCode,
            channel.IsAiAssistantEnabled,
            (long)channel.DefaultPermissions,
            channel.MemberCount,
            channel.LastMessageAt,
            Convert.ToBase64String(channel.Version),
            channel.IconUrl,
            channel.Description);
    }
}
