using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Communication;

public sealed record FriendDto(Guid UserId, string Username, string DisplayName);

public sealed record FriendRequestDto(
    Guid Id,
    Guid SenderId,
    string SenderUsername,
    string SenderDisplayName,
    string? SenderAvatarUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt);

public sealed record SentFriendRequestDto(
    Guid Id,
    Guid ReceiverId,
    string ReceiverUsername,
    string ReceiverDisplayName,
    string? ReceiverAvatarUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? RespondedAt,
    string Version = "");

internal sealed record SendFriendRequestRequestDto(string Username);
internal sealed record FriendRequestResponseDto(Guid Id, Guid TargetUserId, string TargetUsername, string TargetDisplayName);

public interface IFriendService
{
    Observable<IReadOnlyList<FriendDto>> MyFriends { get; }
    bool IsFriend(Guid userId);
    Task RefreshAsync(CancellationToken ct = default);
    Task SendRequestAsync(string username, CancellationToken ct = default);
    Task RemoveAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<FriendRequestDto>> GetReceivedRequestsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SentFriendRequestDto>> GetSentRequestsAsync(CancellationToken ct = default);
    Task AcceptRequestAsync(Guid requestId, CancellationToken ct = default);
    Task DeclineRequestAsync(Guid requestId, CancellationToken ct = default);
    Task CancelRequestAsync(Guid requestId, string version, CancellationToken ct = default);
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

    public async Task<IReadOnlyList<FriendRequestDto>> GetReceivedRequestsAsync(CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<FriendRequestDto>>(
                       "api/v1/friends/requests/received", ct)
                   ?? new List<FriendRequestDto>();
        return list;
    }

    public async Task<IReadOnlyList<SentFriendRequestDto>> GetSentRequestsAsync(CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<SentFriendRequestDto>>(
                       "api/v1/friends/requests/sent", ct)
                   ?? new List<SentFriendRequestDto>();
        return list;
    }

    public async Task AcceptRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync($"api/v1/friends/requests/{requestId}/accept", content: null, ct);
        resp.EnsureSuccessStatusCode();
        await RefreshAsync(ct);
    }

    public async Task DeclineRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync($"api/v1/friends/requests/{requestId}/decline", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task CancelRequestAsync(Guid requestId, string version, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"api/v1/friends/requests/{requestId}")
        {
            Content = JsonContent.Create(new { version }),
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}
