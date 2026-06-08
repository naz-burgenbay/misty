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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyChannels(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetMyChannelsQuery(userId), ct);
        return Ok(result);
    }

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
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new UpdateChannelCommand(
                id,
                userId,
                request.Name,
                request.IsAiAssistantEnabled,
                request.DefaultPermissions,
                request.Version,
                request.Description),
            ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChannel(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeleteChannelCommand(id, userId), ct);
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

    [HttpGet("{id:guid}/permissions/me")]
    [ProducesResponseType(typeof(GetMyChannelPermissionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyEffectivePermissions(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetMyChannelPermissionsQuery(userId, id), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/icon")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadChannelIconResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadIcon(Guid id, IFormFile file, [FromForm] string version, CancellationToken ct)
    {
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new ProblemDetails { Status = 400, Title = "File exceeds 5 MB limit." });

        string[] allowedTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Unsupported image type. Allowed: jpeg, png, webp, gif." });

        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadChannelIconCommand(id, userId, stream, file.ContentType, version), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/icon")]
    [ProducesResponseType(typeof(RemoveChannelIconResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveIcon(Guid id, [FromBody] RemoveChannelIconRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new RemoveChannelIconCommand(id, userId, request.Version), ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/invites")]
    [ProducesResponseType(typeof(ChannelInviteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendInvite(Guid id, SendChannelInviteRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new SendChannelInviteCommand(userId, id, request.Username), ct);
        return CreatedAtAction(nameof(GetChannel), new { id }, result);
    }

    [HttpPost("invites/{id:guid}/accept")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvite(Guid id, [FromBody] AcceptChannelInviteRequest body, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new AcceptChannelInviteCommand(userId, id, body.Version), ct);
        return Ok();
    }

    [HttpPost("invites/{id:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeclineInvite(Guid id, [FromBody] DeclineChannelInviteRequest body, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeclineChannelInviteCommand(userId, id, body.Version), ct);
        return NoContent();
    }
}

public record SendChannelInviteRequest(string Username);
public record AcceptChannelInviteRequest(string Version);
public record DeclineChannelInviteRequest(string Version);
public record RemoveChannelIconRequest(string Version);

public record CreateChannelRequest(
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions);

public record UpdateChannelRequest(
    string Name,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions,
    string Version,
    string? Description);

public record JoinChannelRequest(string? InviteCode);

