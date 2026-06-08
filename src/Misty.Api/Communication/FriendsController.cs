using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/friends")]
[Authorize]
public sealed class FriendsController : ControllerBase
{
    private readonly IMediator _mediator;

    public FriendsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("requests")]
    [ProducesResponseType(typeof(FriendRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendRequest(SendFriendRequestRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new SendFriendRequestCommand(userId, request.Username), ct);
        return CreatedAtAction(nameof(GetReceivedRequests), null, result);
    }

    [HttpPost("requests/{id:guid}/accept")]
    [ProducesResponseType(typeof(FriendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptRequest(Guid id, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new AcceptFriendRequestCommand(userId, id), ct);
        return Ok(result);
    }

    [HttpPost("requests/{id:guid}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeclineRequest(Guid id, [FromBody] DeclineFriendRequestRequest body, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeclineFriendRequestCommand(userId, id, body.Version), ct);
        return NoContent();
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FriendDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFriends(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetFriendsQuery(userId), ct);
        return Ok(result);
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveFriend(Guid userId, CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new RemoveFriendCommand(callerId, userId), ct);
        return NoContent();
    }

    [HttpGet("requests/received")]
    [ProducesResponseType(typeof(IReadOnlyList<FriendRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReceivedRequests(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetReceivedFriendRequestsQuery(userId), ct);
        return Ok(result);
    }

    [HttpGet("requests/sent")]
    [ProducesResponseType(typeof(IReadOnlyList<SentFriendRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSentRequests(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetSentFriendRequestsQuery(userId), ct);
        return Ok(result);
    }

    [HttpDelete("requests/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelRequest(Guid id, [FromBody] CancelFriendRequestRequest body, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new CancelFriendRequestCommand(userId, id, body.Version), ct);
        return NoContent();
    }
}

public record SendFriendRequestRequest(string Username);
public record DeclineFriendRequestRequest(string Version);
public record CancelFriendRequestRequest(string Version);
