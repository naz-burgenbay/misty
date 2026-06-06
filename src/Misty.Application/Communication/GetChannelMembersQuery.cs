using MediatR;
using Misty.Application.Common.Exceptions;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public record GetChannelMembersQuery(Guid ChannelId, Guid ActorId)
    : IRequest<IReadOnlyList<ChannelMemberDto>>;

public record ChannelMemberDto(
    Guid UserId,
    string Username,
    string DisplayName,
    string? AvatarUrl,
    DateTime JoinedAt,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<ModerationActionType> ActiveModerationTypes,
    string Version);

public sealed class GetChannelMembersQueryHandler
    : IRequestHandler<GetChannelMembersQuery, IReadOnlyList<ChannelMemberDto>>
{
    private readonly IMembershipRepository _memberships;
    private readonly IPermissionService _permissions;

    public GetChannelMembersQueryHandler(
        IMembershipRepository memberships,
        IPermissionService permissions)
    {
        _memberships = memberships;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<ChannelMemberDto>> Handle(
        GetChannelMembersQuery query, CancellationToken ct)
    {
        var allowed = await _permissions.CheckPermissionAsync(
            query.ActorId, query.ChannelId, ChannelPermission.ViewChannel, ct);
        if (!allowed)
            throw new ForbiddenException("Missing ViewChannel permission.");

        return await _memberships.ListMembersAsync(query.ChannelId, ct);
    }
}
