using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using System.IdentityModel.Tokens.Jwt;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/members/{userId:guid}/moderation")]
[Authorize]
public sealed class ModerationController : ControllerBase
{
    private readonly IMediator _mediator;

    public ModerationController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(typeof(ApplyModerationActionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Apply(
        Guid channelId,
        Guid userId,
        [FromBody] ApplyModerationActionRequest body,
        CancellationToken ct)
    {
        var issuedBy = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var response = await _mediator.Send(
            new ApplyModerationActionCommand(channelId, userId, issuedBy, body.Type, body.Reason, body.ExpiresAt), ct);
        return CreatedAtAction(nameof(GetActive), new { channelId, userId }, response);
    }

    [HttpDelete("{actionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid channelId, Guid userId, Guid actionId, CancellationToken ct)
    {
        await _mediator.Send(new RevokeModerationActionCommand(channelId, actionId), ct);
        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ModerationActionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive(Guid channelId, Guid userId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetModerationActionsQuery(channelId, userId), ct);
        return Ok(result);
    }
}

public record ApplyModerationActionRequest(ModerationActionType Type, string Reason, DateTime? ExpiresAt);
