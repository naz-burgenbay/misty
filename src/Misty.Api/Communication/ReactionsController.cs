using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Messaging;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/messages/{messageId:guid}/reactions")]
[Authorize]
public sealed class ReactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReactionsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddReaction(
        Guid channelId,
        Guid messageId,
        AddReactionRequest request,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new AddReactionCommand(channelId, messageId, userId, request.EmojiCode), ct);
        return NoContent();
    }

    [HttpDelete("{emojiCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveReaction(
        Guid channelId,
        Guid messageId,
        string emojiCode,
        CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        // The emoji code arrives URL-encoded; ASP.NET Core decodes route segments automatically.
        await _mediator.Send(new RemoveReactionCommand(channelId, messageId, userId, emojiCode), ct);
        return NoContent();
    }
}

public record AddReactionRequest(string EmojiCode);
