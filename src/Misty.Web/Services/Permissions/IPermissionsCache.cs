using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Permissions;

public interface IPermissionsCache
{
    ChannelPermissionFlags Get(Guid channelId);
    Observable<ChannelPermissionFlags> Watch(Guid channelId);
    bool Has(Guid channelId, ChannelPermissionFlags flag);
    void Invalidate(Guid channelId);
}

public sealed class HttpPermissionsCache : IPermissionsCache, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpPermissionsCache> _logger;
    private readonly Dictionary<Guid, Observable<ChannelPermissionFlags>> _byChannel = new();
    private readonly HashSet<Guid> _inFlight = new();
    private readonly object _gate = new();

    private readonly List<IDisposable> _hubSubs = new();

    public HttpPermissionsCache(HttpClient http, ISignalRClient hub, ILogger<HttpPermissionsCache> logger)
    {
        _http = http;
        _logger = logger;

        _hubSubs.Add(hub.OnMembershipChanged(e => Invalidate(e.ChannelId)));
        _hubSubs.Add(hub.OnRoleChanged(e => Invalidate(e.ChannelId)));
        _hubSubs.Add(hub.OnModerationActionApplied(e => Invalidate(e.ChannelId)));
    }

    public ChannelPermissionFlags Get(Guid channelId) => Watch(channelId).Value;

    public Observable<ChannelPermissionFlags> Watch(Guid channelId)
    {
        Observable<ChannelPermissionFlags> obs;
        bool fetch;
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(channelId, out obs!))
            {
                obs = new Observable<ChannelPermissionFlags>(ChannelPermissionFlags.None);
                _byChannel[channelId] = obs;
                fetch = _inFlight.Add(channelId);
            }
            else fetch = false;
        }
        if (fetch) _ = RefreshAsync(channelId);
        return obs;
    }

    public bool Has(Guid channelId, ChannelPermissionFlags flag) => (Get(channelId) & flag) == flag;

    public void Invalidate(Guid channelId)
    {
        lock (_gate)
        {
            if (!_byChannel.ContainsKey(channelId)) return;
            if (!_inFlight.Add(channelId)) return;
        }
        _ = RefreshAsync(channelId);
    }

    private async Task RefreshAsync(Guid channelId)
    {
        try
        {
            var body = await _http.GetFromJsonAsync<PermissionsResponse>(
                $"api/v1/channels/{channelId}/permissions/me");
            if (body is null) return;

            Observable<ChannelPermissionFlags>? obs;
            lock (_gate) { _byChannel.TryGetValue(channelId, out obs); }
            obs?.Set((ChannelPermissionFlags)body.EffectivePermissions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch permissions for channel {ChannelId}.", channelId);
        }
        finally
        {
            lock (_gate) { _inFlight.Remove(channelId); }
        }
    }

    public void Dispose()
    {
        foreach (var sub in _hubSubs) sub.Dispose();
        _hubSubs.Clear();
    }

    private sealed record PermissionsResponse(Guid ChannelId, long EffectivePermissions);
}
