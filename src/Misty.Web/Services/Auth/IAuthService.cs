using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Auth;

// Client-side auth surface. Phase 5 replaces the stub with HttpAuthService, which talks to the API and protects the refresh call with a SemaphoreSlim so concurrent expirations don't trigger a refresh storm.
public interface IAuthService
{
    MockUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task InitializeAsync(CancellationToken ct = default);
    Task SignInAsync(string usernameOrEmail, string password, CancellationToken ct = default);
    Task RegisterAsync(string displayName, string username, string email, string password, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    event Action? AuthStateChanged;
}

public sealed class StubAuthService : IAuthService
{
    private MockUser? _user;
    public MockUser? CurrentUser => _user;
    public bool IsAuthenticated => _user is not null;

    public event Action? AuthStateChanged;

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(_user is null ? null : "stub-access-token");

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SignInAsync(string usernameOrEmail, string password, CancellationToken ct = default)
    {
        _user = new MockUser(Guid.NewGuid(), usernameOrEmail, usernameOrEmail);
        AuthStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task RegisterAsync(string displayName, string username, string email, string password, CancellationToken ct = default)
    {
        _user = new MockUser(Guid.NewGuid(), displayName, username);
        AuthStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        _user = null;
        AuthStateChanged?.Invoke();
        return Task.CompletedTask;
    }
}
