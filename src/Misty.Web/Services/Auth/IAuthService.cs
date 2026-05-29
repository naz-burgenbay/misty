using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Auth;

// Client-side auth surface. Phase 5 replaces this stub with a real implementation that talks to the API and protects the refresh call with a SemaphoreSlim so concurrent expirations don't cause a refresh storm.
public interface IAuthService
{
    MockUser? CurrentUser { get; }
    bool IsAuthenticated { get; }
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task SignInAsync(string email, string password, CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
    event Action? AuthStateChanged;
}

public sealed class StubAuthService : IAuthService
{
    private MockUser? _user = MockDataStore.Me;
    public MockUser? CurrentUser => _user;
    public bool IsAuthenticated => _user is not null;

    public event Action? AuthStateChanged;

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(_user is null ? null : "stub-access-token");

    public Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        _user = MockDataStore.Me;
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
