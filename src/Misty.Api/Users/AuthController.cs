using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Users;

namespace Misty.Api.Users;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(RegisterUserRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(
            new RegisterUserCommand(request.Username, request.Email, request.DisplayName, request.Password),
            ct);

        return Created($"/api/v1/users/{response.UserId}", new { userId = response.UserId });
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(
            new LoginCommand(request.Username, request.Password), ct);

        return Ok(new { accessToken = response.AccessToken, refreshToken = response.RefreshToken, userId = response.UserId });
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var userId = User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value;
        var username = User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)!.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? string.Empty;
        return Ok(new { userId = Guid.Parse(userId), username, email });
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(new RefreshCommand(request.RefreshToken), ct);
        return Ok(new { accessToken = response.AccessToken, refreshToken = response.RefreshToken });
    }
}

public record RegisterUserRequest(string Username, string Email, string DisplayName, string Password);
public record LoginRequest(string Username, string Password);
public record RefreshRequest(string RefreshToken);
