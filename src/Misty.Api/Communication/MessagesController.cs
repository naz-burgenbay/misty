using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Messaging;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/messages")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public MessagesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(GetChannelMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(
        Guid channelId,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetChannelMessagesQuery(channelId, userId, pageSize, cursor), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SendMessage(
        Guid channelId,
        SendChannelMessageRequest request,
        CancellationToken ct)
    {
        var authorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new SendChannelMessageCommand(
                channelId,
                authorId,
                request.Content,
                request.IdempotencyKey,
                request.ParentMessageId),
            ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{messageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EditMessage(
        Guid channelId,
        Guid messageId,
        EditMessageRequest request,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new EditMessageCommand(messageId, channelId, userId, request.Content), ct);
        return NoContent();
    }

    [HttpDelete("{messageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMessage(
        Guid channelId,
        Guid messageId,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeleteMessageCommand(messageId, channelId, userId), ct);
        return NoContent();
    }
}

public record SendChannelMessageRequest(
    string Content,
    string IdempotencyKey,
    Guid? ParentMessageId);

public record EditMessageRequest(string Content);
