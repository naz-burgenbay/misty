using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using Misty.Application.Communication.Contracts;
using Misty.Application.Users;

namespace Misty.Api.Users;

[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchUsersResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        var meId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new SearchUsersQuery(q ?? string.Empty, meId, take), ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetUserByIdResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("me")]
    [ProducesResponseType(typeof(UpdateUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMe(UpdateUserRequest request, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new UpdateUserCommand(userId, request.DisplayName, request.Bio, request.Version), ct);
        return Ok(result);
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMe(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new DeleteUserCommand(userId), ct);
        return NoContent();
    }

    [HttpGet("me/blocks")]
    [ProducesResponseType(typeof(IReadOnlyList<BlockedUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyBlocks(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetBlocksQuery(userId), ct);
        return Ok(result);
    }

    [HttpPost("me/avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadAvatarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
    {
        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new ProblemDetails { Status = 400, Title = "File exceeds 5 MB limit." });

        string[] allowedTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Unsupported image type. Allowed: jpeg, png, webp, gif." });

        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await using var stream = file.OpenReadStream();
        var result = await _mediator.Send(new UploadAvatarCommand(userId, stream, file.ContentType), ct);
        return Ok(result);
    }

    [HttpDelete("me/avatar")]
    [ProducesResponseType(typeof(RemoveAvatarResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new RemoveAvatarCommand(userId), ct);
        return Ok(result);
    }
}

public record UpdateUserRequest(string DisplayName, string? Bio, string Version);
