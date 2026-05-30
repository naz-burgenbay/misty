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

    [HttpPost("{userId:guid}/roles/{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignRole(Guid channelId, Guid userId, Guid roleId, CancellationToken ct)
    {
        await _mediator.Send(new AssignRoleCommand(channelId, userId, roleId), ct);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/roles/{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeRole(Guid channelId, Guid userId, Guid roleId, CancellationToken ct)
    {
        await _mediator.Send(new RevokeRoleCommand(channelId, userId, roleId), ct);
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
