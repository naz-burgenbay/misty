using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Auth;

// Access token lives only in memory, refresh token lives in localStorage. A SemaphoreSlim(1,1) serializes refresh so concurrent expirations of in-flight HTTP calls collapse into a single network refresh.
public sealed class HttpAuthService : IAuthService, IDisposable
{
    private const string RefreshTokenStorageKey = "misty.refreshToken";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly ILocalStorage _storage;
    private readonly ILogger<HttpAuthService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc = DateTime.MinValue;
    private string? _refreshToken;
    private MockUser? _currentUser;

    public HttpAuthService(HttpClient http, ILocalStorage storage, ILogger<HttpAuthService> logger)
    {
        _http = http;
        _storage = storage;
        _logger = logger;
    }

    public MockUser? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser is not null && !string.IsNullOrEmpty(_accessToken);
    public event Action? AuthStateChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _refreshToken = await _storage.GetAsync(RefreshTokenStorageKey);
        if (string.IsNullOrEmpty(_refreshToken))
            return;

        try
        {
            await RefreshAsync(ct);
            if (!string.IsNullOrEmpty(_accessToken))
                await LoadCurrentUserAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Silent refresh on startup failed; clearing stored refresh token.");
            await ClearAsync();
        }

        AuthStateChanged?.Invoke();
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) =>
        GetAccessTokenAsync(forceRefresh: false, ct);

    public async Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow + RefreshSkew < _accessTokenExpiresUtc)
            return _accessToken;

        if (string.IsNullOrEmpty(_refreshToken))
            return _accessToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow + RefreshSkew < _accessTokenExpiresUtc)
                return _accessToken;

            await RefreshAsync(ct);
            return _accessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task SignInAsync(string usernameOrEmail, string password, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/v1/auth/login", new LoginRequestDto(usernameOrEmail, password), ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthException("Invalid username or password.");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: ct)
            ?? throw new AuthException("Empty login response.");

        ApplyTokens(body.AccessToken, body.RefreshToken);
        await _storage.SetAsync(RefreshTokenStorageKey, body.RefreshToken);
        await LoadCurrentUserAsync(ct);
        AuthStateChanged?.Invoke();
    }

    public async Task RegisterAsync(string displayName, string username, string email, string password, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/v1/auth/register",
            new RegisterRequestDto(username, email, displayName, password), ct);

        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new AuthException("That username or email is already taken.");
        if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            throw new AuthException(await ExtractProblemTitleAsync(resp, "Registration failed.", ct));
        resp.EnsureSuccessStatusCode();

        // Auto sign-in so the caller lands authenticated.
        await SignInAsync(username, password, ct);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var tokenToRevoke = _refreshToken;
        if (!string.IsNullOrEmpty(tokenToRevoke))
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync(
                    "api/v1/auth/logout", new LogoutRequestDto(tokenToRevoke), ct);
                // Best-effort revoke; do not fail the local sign-out if the server is unreachable.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Server-side logout failed; clearing local session anyway.");
            }
        }

        await ClearAsync();
        AuthStateChanged?.Invoke();
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_refreshToken))
            throw new AuthException("No refresh token.");

        using var resp = await _http.PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequestDto(_refreshToken), ct);
        if (!resp.IsSuccessStatusCode)
        {
            await ClearAsync();
            throw new AuthException("Refresh rejected.");
        }

        var body = await resp.Content.ReadFromJsonAsync<RefreshResponseDto>(cancellationToken: ct)
            ?? throw new AuthException("Empty refresh response.");

        ApplyTokens(body.AccessToken, body.RefreshToken);
        await _storage.SetAsync(RefreshTokenStorageKey, body.RefreshToken);
    }

    private async Task LoadCurrentUserAsync(CancellationToken ct)
    {
        var me = await SendAuthorizedJsonAsync<MeResponseDto>(HttpMethod.Get, "api/v1/auth/me", ct)
            ?? throw new AuthException("Empty /me response.");

        // The /me endpoint doesn't include display name or bio; fetch the full user record.
        var full = await SendAuthorizedJsonAsync<UserByIdResponseDto>(HttpMethod.Get, $"api/v1/users/{me.UserId}", ct);

        _currentUser = full is null
            ? new MockUser(me.UserId, me.Username, me.Username)
            : new MockUser(full.UserId, full.DisplayName, full.Username, IsAi: false, Bio: full.Bio, AvatarUrl: full.AvatarUrl, Version: full.Version);
    }

    public void UpdateCurrentUser(MockUser user)
    {
        _currentUser = user;
        AuthStateChanged?.Invoke();
    }

    private async Task<T?> SendAuthorizedJsonAsync<T>(HttpMethod method, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(_accessToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private void ApplyTokens(string accessToken, string refreshToken)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _accessTokenExpiresUtc = ReadExpiry(accessToken);
    }

    private static DateTime ReadExpiry(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return DateTime.UtcNow.AddMinutes(15);
            var payload = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            if (payload.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var unix))
                return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            return DateTime.UtcNow.AddMinutes(15);
        }
        catch
        {
            return DateTime.UtcNow.AddMinutes(15);
        }
    }

    private static byte[] Base64UrlDecode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private async Task ClearAsync()
    {
        _accessToken = null;
        _accessTokenExpiresUtc = DateTime.MinValue;
        _refreshToken = null;
        _currentUser = null;
        await _storage.RemoveAsync(RefreshTokenStorageKey);
    }

    private static async Task<string> ExtractProblemTitleAsync(HttpResponseMessage resp, string fallback, CancellationToken ct)
    {
        try
        {
            var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemDto>(cancellationToken: ct);
            if (problem?.Errors is { Count: > 0 } errors)
            {
                var messages = errors.SelectMany(kv => kv.Value).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                if (messages.Count > 0) return string.Join(" ", messages);
            }
            return !string.IsNullOrWhiteSpace(problem?.Title) ? problem!.Title! : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public void Dispose() => _refreshLock.Dispose();

    private sealed record ValidationProblemDto(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

public sealed class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}
