using MediatR;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record GetModerationActionsQuery(Guid ChannelId, Guid TargetUserId)
    : IRequest<List<ModerationActionDto>>;

public record ModerationActionDto(
    Guid ActionId,
    ModerationActionType Type,
    Guid IssuedByUserId,
    string Reason,
    DateTime? ExpiresAt);

public sealed class GetModerationActionsQueryHandler
    : IRequestHandler<GetModerationActionsQuery, List<ModerationActionDto>>
{
    private readonly IModerationRepository _moderation;

    public GetModerationActionsQueryHandler(IModerationRepository moderation)
        => _moderation = moderation;

    public async Task<List<ModerationActionDto>> Handle(
        GetModerationActionsQuery request, CancellationToken ct)
    {
        var actions = await _moderation.GetActiveForUserAsync(
            request.ChannelId, request.TargetUserId, ct);

        return actions
            .Select(a => new ModerationActionDto(a.Id, a.Type, a.IssuedByUserId, a.Reason, a.ExpiresAt))
            .ToList();
    }
}
