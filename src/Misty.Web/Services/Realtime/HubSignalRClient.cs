using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Common;
using SignalRClient = Microsoft.AspNetCore.SignalR.Client;

namespace Misty.Web.Services.Realtime;

public sealed class HubSignalRClient : ISignalRClient, IAsyncDisposable
{
    private readonly IAuthService _auth;
    private readonly ILogger<HubSignalRClient> _logger;
    private readonly string _hubUrl;
    private readonly object _gate = new();
    private HubConnection? _connection;
    private bool _started;

    public Observable<HubConnectionState> State { get; } = new(HubConnectionState.Disconnected);
    public event Action? Reconnected;

    public HubSignalRClient(IAuthService auth, string hubUrl, ILogger<HubSignalRClient> logger)
    {
        _auth = auth;
        _hubUrl = hubUrl;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        HubConnection conn;
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _connection ??= Build();
            conn = _connection;
        }

        State.Set(HubConnectionState.Connecting);
        try
        {
            await conn.StartAsync(ct);
            State.Set(HubConnectionState.Connected);
            _logger.LogInformation("SignalR connected to {Url}.", _hubUrl);
        }
        catch (Exception ex)
        {
            State.Set(HubConnectionState.Disconnected);
            _logger.LogError(ex, "SignalR initial connection failed.");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        HubConnection? conn;
        lock (_gate)
        {
            conn = _connection;
            _connection = null;
            _started = false;
        }
        if (conn is null) return;

        try { await conn.StopAsync(ct); } catch {  }
        await conn.DisposeAsync();
        State.Set(HubConnectionState.Disconnected);
    }

    public IDisposable OnMessageCreated(Action<MessageCreatedEvent> h) => On("MessageCreated", h);
    public IDisposable OnMessageEdited(Action<MessageEditedEvent> h) => On("MessageEdited", h);
    public IDisposable OnMessageDeleted(Action<MessageDeletedEvent> h) => On("MessageDeleted", h);
    public IDisposable OnReactionAdded(Action<ReactionAddedEvent> h) => On("ReactionAdded", h);
    public IDisposable OnReactionRemoved(Action<ReactionRemovedEvent> h) => On("ReactionRemoved", h);
    public IDisposable OnMembershipChanged(Action<PermissionInvalidationEvent> h) => On("MembershipChanged", h);
    public IDisposable OnRoleChanged(Action<PermissionInvalidationEvent> h) => On("RoleChanged", h);
    public IDisposable OnModerationActionApplied(Action<PermissionInvalidationEvent> h) => On("ModerationActionApplied", h);
    public IDisposable OnPresenceChanged(Action<PresenceChangedEvent> h) => On("PresenceChanged", h);
    public IDisposable OnInboxItemReceived(Action<InboxItemReceivedEvent> h) => On("InboxItemReceived", h);

    private IDisposable On<T>(string method, Action<T> handler)
    {
        lock (_gate) { _connection ??= Build(); }
        return _connection!.On(method, handler);
    }

    private HubConnection Build()
    {
        var conn = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = async () => await _auth.GetAccessTokenAsync() ?? string.Empty;
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .ConfigureLogging(lb => lb.SetMinimumLevel(LogLevel.Warning))
            .Build();

        conn.Reconnecting += err =>
        {
            State.Set(HubConnectionState.Reconnecting);
            _logger.LogWarning(err, "SignalR reconnecting.");
            return Task.CompletedTask;
        };
        conn.Reconnected += _ =>
        {
            State.Set(HubConnectionState.Connected);
            _logger.LogInformation("SignalR reconnected.");
            try { Reconnected?.Invoke(); } catch (Exception ex) { _logger.LogError(ex, "Reconnected handler threw."); }
            return Task.CompletedTask;
        };
        conn.Closed += err =>
        {
            State.Set(HubConnectionState.Disconnected);
            if (err is not null) _logger.LogError(err, "SignalR connection closed.");
            return Task.CompletedTask;
        };

        return conn;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }

    private sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Schedule =
        [
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30),
        ];

        public TimeSpan? NextRetryDelay(RetryContext context)
            => context.PreviousRetryCount < Schedule.Length
                ? Schedule[context.PreviousRetryCount]
                : TimeSpan.FromSeconds(60);
    }
}
