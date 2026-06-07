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
    long DefaultPermissions);

internal sealed record UpdateChannelRequestDto(
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled);

public interface IChannelService
{
    Observable<IReadOnlyList<ChannelSummaryDto>> MyChannels { get; }

    Task RefreshAsync(CancellationToken ct = default);
    Task<ChannelSummaryDto> CreateAsync(string name, bool isPrivate, bool aiAssistantEnabled, CancellationToken ct = default);
    Task UpdateAsync(Guid channelId, string name, bool isPrivate, bool aiAssistantEnabled, CancellationToken ct = default);
    Task DeleteAsync(Guid channelId, CancellationToken ct = default);
    Task LeaveAsync(Guid channelId, CancellationToken ct = default);
    ChannelSummaryDto? GetCached(Guid id);
}

public sealed class HttpChannelService : IChannelService
{
    // Mirrors the server's default for new channels until the role editor lands
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

    public async Task<ChannelSummaryDto> CreateAsync(string name, bool isPrivate, bool aiAssistantEnabled, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/v1/channels",
            new CreateChannelRequestDto(name, isPrivate, aiAssistantEnabled, DefaultPermissions), ct);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<CreateChannelResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty create channel response.");

        var summary = new ChannelSummaryDto(created.ChannelId, created.Name, created.IsPrivate,
            created.IsAiAssistantEnabled, MemberCount: 1, LastMessageAt: null);

        // Optimistically prepend so the new channel is immediately selectable; the next RefreshAsync will reconcile.
        var next = new List<ChannelSummaryDto>(MyChannels.Value.Count + 1) { summary };
        next.AddRange(MyChannels.Value.Where(c => c.Id != summary.Id));
        MyChannels.Set(next);
        return summary;
    }

    public async Task UpdateAsync(Guid channelId, string name, bool isPrivate, bool aiAssistantEnabled, CancellationToken ct = default)
    {
        using var resp = await _http.PutAsJsonAsync($"api/v1/channels/{channelId}",
            new UpdateChannelRequestDto(name, isPrivate, aiAssistantEnabled), ct);
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
}
