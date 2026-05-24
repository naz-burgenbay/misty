using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using System.IdentityModel.Tokens.Jwt;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/conversations")]
[Authorize]
public sealed class ConversationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConversationsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(typeof(CreateConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateConversationRequest body,
        CancellationToken ct)
    {
        var requestingUserId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var response = await _mediator.Send(
            new CreateConversationCommand(requestingUserId, body.OtherUserId), ct);
        return Ok(response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetConversationsQuery(userId), ct);
        return Ok(result);
    }
}

public record CreateConversationRequest(Guid OtherUserId);
