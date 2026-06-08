using FluentValidation;
using MediatR;

namespace Misty.Application.Communication;

public record GetMyChannelsQuery(Guid UserId) : IRequest<IReadOnlyList<ChannelSummaryDto>>;

public record ChannelSummaryDto(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    int MemberCount,
    DateTime? LastMessageAt,
    string Version);

public sealed class GetMyChannelsQueryValidator : AbstractValidator<GetMyChannelsQuery>
{
    public GetMyChannelsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class GetMyChannelsQueryHandler
    : IRequestHandler<GetMyChannelsQuery, IReadOnlyList<ChannelSummaryDto>>
{
    private readonly IChannelRepository _channels;

    public GetMyChannelsQueryHandler(IChannelRepository channels) => _channels = channels;

    public async Task<IReadOnlyList<ChannelSummaryDto>> Handle(GetMyChannelsQuery request, CancellationToken ct)
    {
        var channels = await _channels.ListForUserAsync(request.UserId, ct);
        return channels
            .Select(c => new ChannelSummaryDto(
                c.Id, c.Name, c.IsPrivate, c.IsAiAssistantEnabled, c.MemberCount, c.LastMessageAt, Convert.ToBase64String(c.Version)))
            .ToList();
    }
}
