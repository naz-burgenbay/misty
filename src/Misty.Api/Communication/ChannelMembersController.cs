using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/members")]
[Authorize]
public sealed class ChannelMembersController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChannelMembersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid channelId, CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var members = await _mediator.Send(new GetChannelMembersQuery(channelId, actorId), ct);
        return Ok(members);
    }

    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignRole(Guid channelId, Guid userId, Guid roleId, CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new AssignRoleCommand(channelId, actorId, userId, roleId), ct);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeRole(Guid channelId, Guid userId, Guid roleId, CancellationToken ct)
    {
        var actorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new RevokeRoleCommand(channelId, actorId, userId, roleId), ct);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(typeof(KickMemberResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Kick(Guid channelId, Guid userId, KickRequest? request, CancellationToken ct)
    {
        var issuerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? "Kicked from channel." : request!.Reason!;
        var result = await _mediator.Send(new KickMemberCommand(channelId, userId, issuerId, reason), ct);
        return Ok(result);
    }
}

public record KickRequest(string? Reason);
