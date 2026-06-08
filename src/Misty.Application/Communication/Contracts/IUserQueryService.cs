namespace Misty.Application.Communication.Contracts;

public sealed record UserSummary(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl);

public interface IUserQueryService
{
    Task<UserSummary?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);
}
