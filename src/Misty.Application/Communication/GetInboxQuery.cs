using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record GetInboxQuery(Guid UserId, string? Cursor, int Take) : IRequest<InboxPageDto>;

public sealed class GetInboxQueryValidator : AbstractValidator<GetInboxQuery>
{
    public GetInboxQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        // Take is clamped by handler, so no validation needed
    }
}

public sealed class GetInboxQueryHandler : IRequestHandler<GetInboxQuery, InboxPageDto>
{
    private const int MaxTake = 100;
    private const int DefaultTake = 25;

    private readonly IInboxItemRepository _inbox;
    private readonly IUserRepository _users;
    private readonly IChannelQueryService _channels;

    public GetInboxQueryHandler(
        IInboxItemRepository inbox,
        IUserRepository users,
        IChannelQueryService channels)
    {
        _inbox = inbox;
        _users = users;
        _channels = channels;
    }

    public async Task<InboxPageDto> Handle(GetInboxQuery query, CancellationToken ct)
    {
        var take = query.Take <= 0 ? DefaultTake : Math.Min(query.Take, MaxTake);
        var (items, nextCursor) = await _inbox.GetPageAsync(query.UserId, query.Cursor, take, ct);

        if (items.Count == 0)
            return new InboxPageDto(Array.Empty<InboxItemDto>(), nextCursor);

        var actorIds = items.Select(i => i.ActorUserId).Distinct().ToList();
        var actors = new Dictionary<Guid, (string DisplayName, string? AvatarUrl)>();
        foreach (var id in actorIds)
        {
            var u = await _users.GetByIdAsync(id, ct);
            if (u is not null) actors[id] = (u.DisplayName, u.AvatarUrl);
        }

        var dtos = new List<InboxItemDto>(items.Count);
        foreach (var i in items)
        {
            actors.TryGetValue(i.ActorUserId, out var actor);
            var payload = await BuildReferencePayloadAsync(i, ct);
            dtos.Add(new InboxItemDto(
                i.Id,
                i.Type.ToString(),
                i.ActorUserId,
                actor.DisplayName ?? string.Empty,
                actor.AvatarUrl,
                i.ReferenceId,
                payload,
                i.IsActedOn,
                i.CreatedAt));
        }

        return new InboxPageDto(dtos, nextCursor);
    }

    private async Task<object?> BuildReferencePayloadAsync(InboxItem item, CancellationToken ct)
    {
        switch (item.Type)
        {
            case InboxItemType.ChannelInviteReceived when item.ReferenceId is { } channelId:
            {
                var channel = await _channels.GetByIdAsync(channelId, ct);
                return channel is null ? null : new { channelName = channel.Name };
            }
            case InboxItemType.ConversationStarted:
            {
                var peer = await _users.GetByIdAsync(item.ActorUserId, ct);
                return peer is null ? null : new { peerUsername = peer.Username, peerDisplayName = peer.DisplayName };
            }
            default:
                return null;
        }
    }
}
