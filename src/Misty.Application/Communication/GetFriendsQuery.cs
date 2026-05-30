using MediatR;

namespace Misty.Application.Communication;

public record GetFriendsQuery(Guid UserId) : IRequest<IReadOnlyList<FriendDto>>;

public sealed class GetFriendsQueryHandler : IRequestHandler<GetFriendsQuery, IReadOnlyList<FriendDto>>
{
    private readonly IFriendshipRepository _friendships;

    public GetFriendsQueryHandler(IFriendshipRepository friendships) => _friendships = friendships;

    public async Task<IReadOnlyList<FriendDto>> Handle(GetFriendsQuery request, CancellationToken ct)
    {
        var friends = await _friendships.GetFriendsOfAsync(request.UserId, ct);
        return friends
            .Select(f => new FriendDto(f.UserId, f.Username, f.DisplayName, f.AvatarUrl))
            .ToList();
    }
}
