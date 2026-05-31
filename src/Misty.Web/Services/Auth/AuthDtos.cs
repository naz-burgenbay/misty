namespace Misty.Web.Services.Auth;

internal sealed record LoginRequestDto(string Username, string Password);
internal sealed record LoginResponseDto(string AccessToken, string RefreshToken, Guid UserId);

internal sealed record RegisterRequestDto(string Username, string Email, string DisplayName, string Password);
internal sealed record RegisterResponseDto(Guid UserId);

internal sealed record RefreshRequestDto(string RefreshToken);
internal sealed record RefreshResponseDto(string AccessToken, string RefreshToken);

internal sealed record LogoutRequestDto(string RefreshToken);

internal sealed record MeResponseDto(Guid UserId, string Username, string Email);
internal sealed record UserByIdResponseDto(Guid UserId, string Username, string DisplayName, string? Bio, string? AvatarUrl, string Version);
