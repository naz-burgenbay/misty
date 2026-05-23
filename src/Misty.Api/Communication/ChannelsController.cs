using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using Misty.Domain.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels")]
[Authorize]
public sealed class ChannelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChannelsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(typeof(CreateChannelResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateChannel(CreateChannelRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new CreateChannelCommand(
                userId,
                request.Name,
                request.IsPrivate,
                request.IsAiAssistantEnabled,
                request.DefaultPermissions),
            ct);
        return CreatedAtAction(nameof(GetChannel), new { id = result.ChannelId }, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetChannelByIdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChannel(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetChannelByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UpdateChannelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateChannel(Guid id, UpdateChannelRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new UpdateChannelCommand(
                id,
                request.Name,
                request.IsAiAssistantEnabled,
                request.DefaultPermissions,
                request.Version),
            ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChannel(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteChannelCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/join")]
    [ProducesResponseType(typeof(JoinChannelResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JoinChannel(Guid id, JoinChannelRequest? request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new JoinChannelCommand(userId, id, request?.InviteCode), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaveChannel(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new LeaveChannelCommand(userId, id), ct);
        return NoContent();
    }
}

public record CreateChannelRequest(
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions);

public record UpdateChannelRequest(
    string Name,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions,
    string Version);

public record JoinChannelRequest(string? InviteCode);

