using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/inbox")]
[Authorize]
public sealed class InboxController : ControllerBase
{
    private readonly IMediator _mediator;

    public InboxController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(InboxPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] string? cursor, [FromQuery] int take = 25, CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetInboxQuery(userId, cursor, take), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DismissInboxItemCommand(userId, id), ct);
        return NoContent();
    }
}
