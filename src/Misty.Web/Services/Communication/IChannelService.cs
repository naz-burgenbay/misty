using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Common;

namespace Misty.Web.Services.Communication;

public sealed record ChannelSummaryDto(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    int MemberCount,
    DateTime? LastMessageAt);

public sealed record CreateChannelResponseDto(
    Guid ChannelId,
    string Name,
    bool IsPrivate,
    string? InviteCode,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    string Version);

internal sealed record CreateChannelRequestDto(
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    string? Description = null);

internal sealed record UpdateChannelRequestDto(
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    long DefaultPermissions,
    string Version,
    string? Description);

internal sealed record GetChannelByIdResponseDto(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    int MemberCount,
    DateTime? LastMessageAt,
    string? Description,
    string? IconUrl,
    string Version);

public sealed record ChannelDetailDto(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    int MemberCount,
    DateTime? LastMessageAt,
    string? Description,
    string? IconUrl,
    string Version);

internal sealed record ChannelInviteRequestDto(string Username);
internal sealed record AcceptDeclineInviteRequestDto(string Version);
internal sealed record RemoveChannelIconRequestDto(string Version);
internal sealed record UploadChannelIconResponseDto(string IconUrl, string Version);
internal sealed record RemoveChannelIconResponseDto(string Version);
internal sealed record KickMemberRequestDto(string? Reason);

public interface IChannelService
{
    Observable<IReadOnlyList<ChannelSummaryDto>> MyChannels { get; }

    Task RefreshAsync(CancellationToken ct = default);
    Task<ChannelDetailDto?> GetDetailAsync(Guid channelId, CancellationToken ct = default);
    Task<ChannelSummaryDto> CreateAsync(string name, bool isPrivate, bool aiAssistantEnabled, string? description = null, CancellationToken ct = default);
    Task UpdateAsync(Guid channelId, string name, bool isPrivate, bool aiAssistantEnabled, string? description, string version, CancellationToken ct = default);
    Task DeleteAsync(Guid channelId, CancellationToken ct = default);
    Task LeaveAsync(Guid channelId, CancellationToken ct = default);
    ChannelSummaryDto? GetCached(Guid id);
    
    Task SendInviteAsync(Guid channelId, string username, CancellationToken ct = default);
    
    Task AcceptInviteAsync(Guid inviteId, string version, CancellationToken ct = default);
    
    Task DeclineInviteAsync(Guid inviteId, string version, CancellationToken ct = default);
    
    Task KickMemberAsync(Guid channelId, Guid userId, CancellationToken ct = default);
    
    Task<string> UploadIconAsync(Guid channelId, Stream content, string contentType, string version, CancellationToken ct = default);
    
    Task<string> RemoveIconAsync(Guid channelId, string version, CancellationToken ct = default);
}

public sealed class HttpChannelService : IChannelService
{
    private const long DefaultPermissions = 7;

    private readonly HttpClient _http;
    private readonly ILogger<HttpChannelService> _logger;

    public Observable<IReadOnlyList<ChannelSummaryDto>> MyChannels { get; } =
        new(Array.Empty<ChannelSummaryDto>());

    public HttpChannelService(HttpClient http, ILogger<HttpChannelService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _http.GetFromJsonAsync<List<ChannelSummaryDto>>("api/v1/channels", ct)
                       ?? new List<ChannelSummaryDto>();
            MyChannels.Set(list);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            MyChannels.Set(Array.Empty<ChannelSummaryDto>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load channel list.");
            throw;
        }
    }

    public async Task<ChannelSummaryDto> CreateAsync(string name, bool isPrivate, bool aiAssistantEnabled, string? description = null, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/v1/channels",
            new CreateChannelRequestDto(name, isPrivate, aiAssistantEnabled, DefaultPermissions, description), ct);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<CreateChannelResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty create channel response.");

        var summary = new ChannelSummaryDto(created.ChannelId, created.Name, created.IsPrivate,
            created.IsAiAssistantEnabled, MemberCount: 1, LastMessageAt: null);

        var next = new List<ChannelSummaryDto>(MyChannels.Value.Count + 1) { summary };
        next.AddRange(MyChannels.Value.Where(c => c.Id != summary.Id));
        MyChannels.Set(next);
        return summary;
    }

    public async Task<ChannelDetailDto?> GetDetailAsync(Guid channelId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"api/v1/channels/{channelId}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<GetChannelByIdResponseDto>(cancellationToken: ct);
            if (body is null) return null;
            return new ChannelDetailDto(body.Id, body.Name, body.IsPrivate, body.IsAiAssistantEnabled,
                body.MemberCount, body.LastMessageAt, body.Description, body.IconUrl, body.Version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch channel detail for {ChannelId}.", channelId);
            return null;
        }
    }

    public async Task UpdateAsync(Guid channelId, string name, bool isPrivate, bool aiAssistantEnabled, string? description, string version, CancellationToken ct = default)
    {
        using var resp = await _http.PutAsJsonAsync($"api/v1/channels/{channelId}",
            new UpdateChannelRequestDto(name, isPrivate, aiAssistantEnabled, DefaultPermissions, version, description), ct);
        resp.EnsureSuccessStatusCode();

        var updated = MyChannels.Value.Select(c => c.Id == channelId
            ? c with { Name = name, IsPrivate = isPrivate, IsAiAssistantEnabled = aiAssistantEnabled }
            : c).ToList();
        MyChannels.Set(updated);
    }

    public async Task DeleteAsync(Guid channelId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/v1/channels/{channelId}", ct);
        resp.EnsureSuccessStatusCode();

        var updated = MyChannels.Value.Where(c => c.Id != channelId).ToList();
        MyChannels.Set(updated);
    }

    public async Task LeaveAsync(Guid channelId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/v1/channels/{channelId}/leave", ct);
        resp.EnsureSuccessStatusCode();

        var updated = MyChannels.Value.Where(c => c.Id != channelId).ToList();
        MyChannels.Set(updated);
    }

    public ChannelSummaryDto? GetCached(Guid id) => MyChannels.Value.FirstOrDefault(c => c.Id == id);

    public async Task SendInviteAsync(Guid channelId, string username, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/{channelId}/invites",
            new ChannelInviteRequestDto(username), ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task AcceptInviteAsync(Guid inviteId, string version, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/invites/{inviteId}/accept",
            new AcceptDeclineInviteRequestDto(version), ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeclineInviteAsync(Guid inviteId, string version, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/invites/{inviteId}/decline",
            new AcceptDeclineInviteRequestDto(version), ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task KickMemberAsync(Guid channelId, Guid userId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"api/v1/channels/{channelId}/members/{userId}")
        {
            Content = JsonContent.Create(new KickMemberRequestDto(null)),
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<string> UploadIconAsync(Guid channelId, Stream content, string contentType, string version, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", "icon");
        form.Add(new StringContent(version), "version");

        using var resp = await _http.PostAsync($"api/v1/channels/{channelId}/icon", form, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<UploadChannelIconResponseDto>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Empty upload icon response.");
        return result.Version;
    }

    public async Task<string> RemoveIconAsync(Guid channelId, string version, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"api/v1/channels/{channelId}/icon")
        {
            Content = JsonContent.Create(new RemoveChannelIconRequestDto(version)),
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<RemoveChannelIconResponseDto>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Empty remove icon response.");
        return result.Version;
    }
}
