using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Auth;

public interface IAuthService
{
    MockUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task InitializeAsync(CancellationToken ct = default);
    Task SignInAsync(string usernameOrEmail, string password, CancellationToken ct = default);
    Task RegisterAsync(string displayName, string username, string email, string password, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    void UpdateCurrentUser(MockUser user);
    event Action? AuthStateChanged;
}

