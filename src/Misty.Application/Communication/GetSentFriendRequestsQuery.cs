using MediatR;
using Misty.Application.Users;

namespace Misty.Application.Communication;

public record GetSentFriendRequestsQuery(Guid UserId) : IRequest<IReadOnlyList<SentFriendRequestDto>>;

public sealed class GetSentFriendRequestsQueryHandler
    : IRequestHandler<GetSentFriendRequestsQuery, IReadOnlyList<SentFriendRequestDto>>
{
    private readonly IFriendRequestRepository _requests;
    private readonly IUserRepository _users;

    public GetSentFriendRequestsQueryHandler(IFriendRequestRepository requests, IUserRepository users)
    {
        _requests = requests;
        _users = users;
    }

    public async Task<IReadOnlyList<SentFriendRequestDto>> Handle(GetSentFriendRequestsQuery query, CancellationToken ct)
    {
        var rows = await _requests.GetPendingSentAsync(query.UserId, ct);
        if (rows.Count == 0) return Array.Empty<SentFriendRequestDto>();

        var receiverIds = rows.Select(r => r.ReceiverId).Distinct().ToList();
        var receivers = new Dictionary<Guid, (string Username, string DisplayName, string? AvatarUrl)>();
        foreach (var rid in receiverIds)
        {
            var u = await _users.GetByIdAsync(rid, ct);
            if (u is not null)
                receivers[rid] = (u.Username, u.DisplayName, u.AvatarUrl);
        }

        return rows
            .Select(r =>
            {
                receivers.TryGetValue(r.ReceiverId, out var u);
                return new SentFriendRequestDto(
                    r.Id,
                    r.ReceiverId,
                    u.Username ?? string.Empty,
                    u.DisplayName ?? string.Empty,
                    u.AvatarUrl,
                    r.Status.ToString(),
                    r.CreatedAt,
                    r.RespondedAt,
                    Convert.ToBase64String(r.Version));
            })
            .ToList();
    }
}
