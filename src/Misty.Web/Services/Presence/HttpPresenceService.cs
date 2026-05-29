using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Presence;

// Presence is eventually consistent. Watch(userId) returns Online/Offline immediately if cached, otherwise Unknown: and queues a debounced bulk fetch so the next render pulls the real state. Live updates arrive via the SignalR PresenceChanged event, which is broadcast to every authenticated connection.
public sealed class HttpPresenceService : IPresenceService, IDisposable
{
    private readonly HttpClient _http;
    private readonly ISignalRClient _hub;
    private readonly IAuthService _auth;
    private readonly ILogger<HttpPresenceService> _logger;

    private readonly Dictionary<Guid, Observable<PresenceState>> _byUser = new();
    private readonly HashSet<Guid> _pending = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _flushCts;
    private IDisposable? _hubSub;

    public HttpPresenceService(HttpClient http, ISignalRClient hub, IAuthService auth,
        ILogger<HttpPresenceService> logger)
    {
        _http = http;
        _hub = hub;
        _auth = auth;
        _logger = logger;
    }

    public PresenceState Get(Guid userId)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userId, out var obs) ? obs.Value : PresenceState.Unknown;
        }
    }

    public Observable<PresenceState> Watch(Guid userId)
    {
        Observable<PresenceState> obs;
        bool fetch;
        lock (_gate)
        {
            EnsureHubSubscribed();
            if (!_byUser.TryGetValue(userId, out var existing))
            {
                obs = new Observable<PresenceState>(PresenceState.Unknown);
                _byUser[userId] = obs;
                _pending.Add(userId);
                fetch = true;
            }
            else
            {
                obs = existing;
                fetch = false;
            }
        }

        if (fetch) ScheduleFlush();
        return obs;
    }

    private void EnsureHubSubscribed()
    {
        if (_hubSub is not null) return;
        _hubSub = _hub.OnPresenceChanged(OnPresenceChanged);
    }

    private void OnPresenceChanged(PresenceChangedEvent evt)
    {
        Observable<PresenceState>? obs;
        lock (_gate)
        {
            if (!_byUser.TryGetValue(evt.UserId, out obs))
            {
                // Nobody's watching yet; seed the cache so a later Watch call sees the right state without a fetch round-trip.
                obs = new Observable<PresenceState>(evt.IsOnline ? PresenceState.Online : PresenceState.Offline);
                _byUser[evt.UserId] = obs;
                return;
            }
        }

        obs.Set(evt.IsOnline ? PresenceState.Online : PresenceState.Offline);
    }

    private void ScheduleFlush()
    {
        if (!_auth.IsAuthenticated) return;

        CancellationTokenSource? oldCts;
        CancellationTokenSource newCts = new();
        lock (_gate)
        {
            oldCts = _flushCts;
            _flushCts = newCts;
        }
        oldCts?.Cancel();
        _ = FlushAfterDelayAsync(newCts.Token);
    }

    private async Task FlushAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(80, ct);
        }
        catch (TaskCanceledException) { return; }

        List<Guid> ids;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            ids = new List<Guid>(_pending);
            _pending.Clear();
        }

        try
        {
            using var resp = await _http.PostAsJsonAsync("api/v1/presence/bulk",
                new BulkRequest(ids), ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<BulkResponse>(cancellationToken: ct);
            if (body is null) return;

            foreach (var s in body.Statuses)
            {
                Observable<PresenceState>? obs;
                lock (_gate)
                {
                    if (!_byUser.TryGetValue(s.UserId, out obs)) continue;
                }
                obs.Set(s.IsOnline ? PresenceState.Online : PresenceState.Offline);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bulk presence fetch failed.");
        }
    }

    public void Dispose() => _hubSub?.Dispose();

    private sealed record BulkRequest(IReadOnlyList<Guid> UserIds);
    private sealed record BulkResponse(IReadOnlyList<PresenceStatusDto> Statuses);
    private sealed record PresenceStatusDto(Guid UserId, bool IsOnline);
}
