using MediatR;
using Misty.Application.Users;

namespace Misty.Application.Communication;

public record GetReceivedFriendRequestsQuery(Guid UserId) : IRequest<IReadOnlyList<FriendRequestDto>>;

public sealed class GetReceivedFriendRequestsQueryHandler
    : IRequestHandler<GetReceivedFriendRequestsQuery, IReadOnlyList<FriendRequestDto>>
{
    private readonly IFriendRequestRepository _requests;
    private readonly IUserRepository _users;

    public GetReceivedFriendRequestsQueryHandler(IFriendRequestRepository requests, IUserRepository users)
    {
        _requests = requests;
        _users = users;
    }

    public async Task<IReadOnlyList<FriendRequestDto>> Handle(GetReceivedFriendRequestsQuery query, CancellationToken ct)
    {
        var rows = await _requests.GetPendingReceivedAsync(query.UserId, ct);
        if (rows.Count == 0) return Array.Empty<FriendRequestDto>();

        var senderIds = rows.Select(r => r.SenderId).Distinct().ToList();
        var senders = new Dictionary<Guid, (string Username, string DisplayName, string? AvatarUrl)>();
        foreach (var sid in senderIds)
        {
            var u = await _users.GetByIdAsync(sid, ct);
            if (u is not null)
                senders[sid] = (u.Username, u.DisplayName, u.AvatarUrl);
        }

        return rows
            .Select(r =>
            {
                senders.TryGetValue(r.SenderId, out var s);
                return new FriendRequestDto(
                    r.Id,
                    r.SenderId,
                    s.Username ?? string.Empty,
                    s.DisplayName ?? string.Empty,
                    s.AvatarUrl,
                    r.Status.ToString(),
                    r.CreatedAt,
                    r.RespondedAt,
                    Convert.ToBase64String(r.Version));
            })
            .ToList();
    }
}
