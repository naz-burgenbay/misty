using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Communication;

public sealed record FriendDto(Guid UserId, string Username, string DisplayName);

internal sealed record SendFriendRequestRequestDto(string Username);
internal sealed record FriendRequestResponseDto(Guid Id, Guid TargetUserId, string TargetUsername, string TargetDisplayName);

public interface IFriendService
{
    Observable<IReadOnlyList<FriendDto>> MyFriends { get; }
    bool IsFriend(Guid userId);
    Task RefreshAsync(CancellationToken ct = default);
    Task SendRequestAsync(string username, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, CancellationToken ct = default);
}

public sealed class HttpFriendService : IFriendService
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpFriendService> _logger;

    public Observable<IReadOnlyList<FriendDto>> MyFriends { get; } =
        new(Array.Empty<FriendDto>());

    public HttpFriendService(HttpClient http, ILogger<HttpFriendService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public bool IsFriend(Guid userId) => MyFriends.Value.Any(f => f.UserId == userId);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<FriendDto>>("api/v1/friends", ct)
                       ?? new List<FriendDto>();
            MyFriends.Set(list);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            MyFriends.Set(Array.Empty<FriendDto>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh friend list.");
        }
    }

    public async Task SendRequestAsync(string username, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/v1/friends/requests", new SendFriendRequestRequestDto(username), ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RemoveAsync(Guid userId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/v1/friends/{userId}", ct);
        resp.EnsureSuccessStatusCode();
        MyFriends.Set(MyFriends.Value.Where(f => f.UserId != userId).ToList());
    }
}
