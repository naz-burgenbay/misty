using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record GetMyChannelPermissionsQuery(Guid UserId, Guid ChannelId)
    : IRequest<GetMyChannelPermissionsResponse>;

public sealed class GetMyChannelPermissionsQueryValidator : AbstractValidator<GetMyChannelPermissionsQuery>
{
    public GetMyChannelPermissionsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ChannelId).NotEmpty();
    }
}

public record GetMyChannelPermissionsResponse(Guid ChannelId, long EffectivePermissions);

public sealed class GetMyChannelPermissionsQueryHandler
    : IRequestHandler<GetMyChannelPermissionsQuery, GetMyChannelPermissionsResponse>
{
    private readonly IPermissionService _permissions;

    public GetMyChannelPermissionsQueryHandler(IPermissionService permissions)
        => _permissions = permissions;

    public async Task<GetMyChannelPermissionsResponse> Handle(
        GetMyChannelPermissionsQuery request, CancellationToken ct)
    {
        var effective = await _permissions.GetEffectivePermissionsAsync(
            request.UserId, request.ChannelId, ct);
        return new GetMyChannelPermissionsResponse(request.ChannelId, (long)effective);
    }
}
