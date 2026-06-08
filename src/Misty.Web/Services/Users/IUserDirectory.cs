using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Misty.Web.Services.Users;

public sealed record UserSummary(Guid Id, string DisplayName, string Username, bool IsAi = false);

public interface IUserDirectory
{
    UserSummary Get(Guid id);
    Task EnsureAsync(Guid id, CancellationToken ct = default);
    Task<UserPublicProfile?> GetProfileAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<UserSummaryWithAvatar>> SearchAsync(string query, int take = 10, CancellationToken ct = default);
    void Seed(UserSummary user);
    event Action<Guid>? Updated;
}

public sealed record UserSummaryWithAvatar(Guid Id, string DisplayName, string Username, string? AvatarUrl);

public sealed record UserPublicProfile(Guid Id, string DisplayName, string Username, string? Bio, string? AvatarUrl);

public sealed class HttpUserDirectory : IUserDirectory
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpUserDirectory> _logger;
    private readonly Dictionary<Guid, UserSummary> _cache = new();
    private readonly HashSet<Guid> _inFlight = new();

    public event Action<Guid>? Updated;

    public HttpUserDirectory(HttpClient http, ILogger<HttpUserDirectory> logger)
    {
        _http = http;
        _logger = logger;
    }

    public UserSummary Get(Guid id)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(id, out var u)) return u;
        }
        var shortId = id.ToString("N")[..6];
        return new UserSummary(id, $"User {shortId}", shortId);
    }

    public void Seed(UserSummary user)
    {
        bool changed;
        lock (_cache)
        {
            changed = !_cache.TryGetValue(user.Id, out var existing) || existing != user;
            _cache[user.Id] = user;
        }
        if (changed) Updated?.Invoke(user.Id);
    }

    public async Task EnsureAsync(Guid id, CancellationToken ct = default)
    {
        lock (_cache)
        {
            if (_cache.ContainsKey(id) || !_inFlight.Add(id)) return;
        }

        try
        {
            var resp = await _http.GetAsync($"api/v1/users/{id}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<UserByIdDto>(cancellationToken: ct);
            if (body is null) return;
            Seed(new UserSummary(body.UserId, body.DisplayName, body.Username));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user {UserId}.", id);
        }
        finally
        {
            lock (_cache) { _inFlight.Remove(id); }
        }
    }

    private sealed record UserByIdDto(Guid UserId, string Username, string DisplayName, string? Bio, string? AvatarUrl, string Version);

    public async Task<UserPublicProfile?> GetProfileAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"api/v1/users/{id}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<UserByIdDto>(cancellationToken: ct);
            if (body is null) return null;
            Seed(new UserSummary(body.UserId, body.DisplayName, body.Username));
            return new UserPublicProfile(body.UserId, body.DisplayName, body.Username, body.Bio, body.AvatarUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch profile for user {UserId}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<UserSummaryWithAvatar>> SearchAsync(string query, int take = 10, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/v1/users/search?q={Uri.EscapeDataString(query ?? string.Empty)}&take={take}";
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<UserSummaryWithAvatar>();
            var body = await resp.Content.ReadFromJsonAsync<SearchResponseDto>(cancellationToken: ct);
            if (body is null) return Array.Empty<UserSummaryWithAvatar>();
            foreach (var match in body.Results)
                Seed(new UserSummary(match.UserId, match.DisplayName, match.Username));
            return body.Results
                .Select(m => new UserSummaryWithAvatar(m.UserId, m.DisplayName, m.Username, m.AvatarUrl))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "User search failed for query '{Query}'.", query);
            return Array.Empty<UserSummaryWithAvatar>();
        }
    }

    private sealed record SearchResponseDto(IReadOnlyList<UserSearchMatchDto> Results);
    private sealed record UserSearchMatchDto(Guid UserId, string Username, string DisplayName, string? AvatarUrl);
}
