using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Auth;
using Misty.Web.Services.Common;
using Misty.Web.Services.MockData;
using Misty.Web.Services.Realtime;
using Misty.Web.Services.Users;

namespace Misty.Web.Services.Messaging;

// Per-conversation observable message list. Optimistic insert produces a local message with a client-generated idempotency key; when the SignalR MessageCreated event arrives (or the 201 response with the persisted Id), the optimistic entry is replaced atomically using the key (SignalR-vs-201 dedup as described in the design system).
//
// Channel ids must be marked via EnsureLoadedAsync before they go through the API path; ids that are never registered (e.g. DM conversations until Step 5.5) keep the legacy mock behaviour so DirectMessage.razor continues to render with fake data.
public interface IMessageStore
{
    Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId);
    Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default);
    Task LoadOlderAsync(Guid channelId, CancellationToken ct = default);
    bool HasMoreOlder(Guid channelId);
    Task SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default);
}

public sealed class StubMessageStore : IMessageStore
{
    private readonly Dictionary<Guid, Observable<IReadOnlyList<MockMessage>>> _byConversation = new();

    public Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId)
    {
        if (!_byConversation.TryGetValue(conversationId, out var obs))
        {
            obs = new Observable<IReadOnlyList<MockMessage>>(MockDataStore.GetMessages(conversationId));
            _byConversation[conversationId] = obs;
        }
        return obs;
    }

    public Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default) => Task.CompletedTask;
    public Task LoadOlderAsync(Guid channelId, CancellationToken ct = default) => Task.CompletedTask;
    public bool HasMoreOlder(Guid channelId) => false;

    public Task SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default)
    {
        var obs = GetConversation(conversationId);
        var optimistic = new MockMessage(Guid.NewGuid(), MockDataStore.MeId, content,
            DateTime.UtcNow, ParentMessageId: parentMessageId);
        obs.Set(obs.Value.Append(optimistic).ToList());
        return Task.CompletedTask;
    }
}

public sealed class HttpMessageStore : IMessageStore, IDisposable
{
    private const int PageSize = 50;

    private readonly HttpClient _http;
    private readonly ISignalRClient _hub;
    private readonly IAuthService _auth;
    private readonly IUserDirectory _users;
    private readonly ILogger<HttpMessageStore> _logger;

    private readonly Dictionary<Guid, Observable<IReadOnlyList<MockMessage>>> _byConversation = new();
    private readonly Dictionary<Guid, ChannelState> _channels = new();
    // Pending optimistic sends keyed by idempotency key, scoped per channel, so the 201 echo can swap the placeholder Id without duplicating the row.
    private readonly Dictionary<Guid, Dictionary<string, Guid>> _pendingByChannel = new();

    private readonly List<IDisposable> _hubSubs = new();
    private bool _hubSubscribed;

    public HttpMessageStore(HttpClient http, ISignalRClient hub, IAuthService auth, IUserDirectory users, ILogger<HttpMessageStore> logger)
    {
        _http = http;
        _hub = hub;
        _auth = auth;
        _users = users;
        _logger = logger;

        _hub.Reconnected += OnHubReconnected;
    }

    public Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId)
    {
        if (!_byConversation.TryGetValue(conversationId, out var obs))
        {
            // For unregistered ids (DMs in this phase) fall back to mock data so the existing DM screen keeps working until Step 5.5.
            var seed = _channels.ContainsKey(conversationId)
                ? (IReadOnlyList<MockMessage>)Array.Empty<MockMessage>()
                : MockDataStore.GetMessages(conversationId);
            obs = new Observable<IReadOnlyList<MockMessage>>(seed);
            _byConversation[conversationId] = obs;
        }
        return obs;
    }

    public async Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default)
    {
        EnsureHubSubscribed();

        if (_channels.TryGetValue(channelId, out var state) && state.InitialLoaded)
            return;

        state ??= new ChannelState();
        _channels[channelId] = state;

        var obs = GetConversation(channelId);
        try
        {
            var page = await FetchPageAsync(channelId, cursor: null, ct);
            state.NextCursor = page.NextCursor;
            state.InitialLoaded = true;

            var mapped = page.Messages.Select(ToMockMessage).ToList();
            obs.Set(mapped);
            ScheduleUserPreload(mapped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load messages for channel {ChannelId}.", channelId);
            throw;
        }
    }

    public async Task LoadOlderAsync(Guid channelId, CancellationToken ct = default)
    {
        if (!_channels.TryGetValue(channelId, out var state) || !state.InitialLoaded) return;
        if (state.NextCursor is null || state.LoadingOlder) return;

        state.LoadingOlder = true;
        try
        {
            var page = await FetchPageAsync(channelId, state.NextCursor, ct);
            state.NextCursor = page.NextCursor;

            if (page.Messages.Count == 0) return;
            var obs = GetConversation(channelId);
            var older = page.Messages.Select(ToMockMessage).ToList();
            // The cursor API returns pages in DESC order (newest first within the page); we keep the visible list in ASC order, so older messages prepend.
            var merged = new List<MockMessage>(older.Count + obs.Value.Count);
            merged.AddRange(older);
            merged.AddRange(obs.Value);
            obs.Set(merged);
            ScheduleUserPreload(older);
        }
        finally
        {
            state.LoadingOlder = false;
        }
    }

    public bool HasMoreOlder(Guid channelId)
        => _channels.TryGetValue(channelId, out var s) && s.InitialLoaded && s.NextCursor is not null;

    public async Task SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default)
    {
        // Unregistered conversations (DMs) keep the mock optimistic behaviour for now.
        if (!_channels.ContainsKey(conversationId))
        {
            var obs2 = GetConversation(conversationId);
            var optimistic2 = new MockMessage(Guid.NewGuid(), MockDataStore.MeId, content,
                DateTime.UtcNow, ParentMessageId: parentMessageId);
            obs2.Set(obs2.Value.Append(optimistic2).ToList());
            return;
        }

        var meId = _auth.CurrentUser?.Id ?? Guid.Empty;
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var optimisticId = Guid.NewGuid();

        var obs = GetConversation(conversationId);
        var parentPreview = parentMessageId is { } pid
            ? obs.Value.Where(m => m.Id == pid).Select(m => new MockParentPreview(
                m.Id, m.AuthorId,
                m.IsTombstone ? string.Empty : m.Content,
                m.IsTombstone)).FirstOrDefault()
            : null;

        var optimistic = new MockMessage(optimisticId, meId, content, DateTime.UtcNow,
            ParentMessageId: parentMessageId, ParentPreview: parentPreview);
        obs.Set(obs.Value.Append(optimistic).ToList());
        TrackPending(conversationId, idempotencyKey, optimisticId);

        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"api/v1/channels/{conversationId}/messages",
                new SendMessageRequestDto(content, idempotencyKey, parentMessageId),
                ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<SendMessageResponseDto>(cancellationToken: ct);
            if (body is not null)
                ReplaceOptimistic(conversationId, idempotencyKey, body.MessageId, body.Content, body.CreatedAt, body.ParentMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send to channel {ChannelId} failed; rolling back optimistic message.", conversationId);
            RemoveOptimistic(conversationId, optimisticId);
            ClearPending(conversationId, idempotencyKey);
            throw;
        }
    }

    private void EnsureHubSubscribed()
    {
        if (_hubSubscribed) return;
        _hubSubscribed = true;

        _hubSubs.Add(_hub.OnMessageCreated(OnMessageCreated));
        _hubSubs.Add(_hub.OnMessageEdited(OnMessageEdited));
        _hubSubs.Add(_hub.OnMessageDeleted(OnMessageDeleted));
    }

    private void OnMessageCreated(MessageCreatedEvent evt)
    {
        if (evt.ChannelId is not { } channelId) return;
        if (!_channels.ContainsKey(channelId)) return;

        var obs = GetConversation(channelId);
        var existing = obs.Value;

        // Echo of a message we just sent: the optimistic row was already swapped to the real Id by the 201 path; skip.
        if (existing.Any(m => m.Id == evt.MessageId)) return;

        // SignalR payload doesn't carry the parent preview; backfill from the loaded window when possible.
        MockParentPreview? preview = null;
        if (evt.ParentMessageId is { } parentId)
        {
            var parent = existing.FirstOrDefault(m => m.Id == parentId);
            if (parent is not null)
            {
                preview = new MockParentPreview(
                    parent.Id, parent.AuthorId,
                    parent.IsTombstone ? string.Empty : parent.Content,
                    parent.IsTombstone);
            }
        }

        var msg = new MockMessage(evt.MessageId, evt.AuthorId, evt.Content, evt.CreatedAt,
            ParentMessageId: evt.ParentMessageId, ParentPreview: preview);
        var next = new List<MockMessage>(existing.Count + 1);
        next.AddRange(existing);
        next.Add(msg);
        obs.Set(next);
        _ = _users.EnsureAsync(evt.AuthorId);
    }

    private void OnMessageEdited(MessageEditedEvent evt)
    {
        if (evt.ChannelId is not { } channelId) return;
        if (!_byConversation.TryGetValue(channelId, out var obs)) return;
        var next = obs.Value.Select(m => m.Id == evt.MessageId
            ? m with { Content = evt.Content, IsEdited = true }
            : m).ToList();
        obs.Set(next);
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        if (evt.ChannelId is not { } channelId) return;
        if (!_byConversation.TryGetValue(channelId, out var obs)) return;
        var next = evt.IsTombstone
            ? obs.Value.Select(m => m.Id == evt.MessageId
                ? m with { Content = string.Empty, IsTombstone = true }
                : m).ToList()
            : obs.Value.Where(m => m.Id != evt.MessageId).ToList();
        obs.Set(next);
    }

    private void OnHubReconnected()
    {
        // On reconnect, refetch every loaded channel from the cursor head. Simpler than reasoning about gap recovery for the small page sizes this client uses.
        foreach (var channelId in _channels.Keys.ToList())
        {
            _channels[channelId] = new ChannelState();
            _ = EnsureLoadedAsync(channelId);
        }
    }

    private async Task<MessagesPageDto> FetchPageAsync(Guid channelId, string? cursor, CancellationToken ct)
    {
        var url = cursor is null
            ? $"api/v1/channels/{channelId}/messages?pageSize={PageSize}"
            : $"api/v1/channels/{channelId}/messages?pageSize={PageSize}&cursor={WebUtility.UrlEncode(cursor)}";
        return await _http.GetFromJsonAsync<MessagesPageDto>(url, ct)
               ?? new MessagesPageDto(new List<MessageWireDto>(), null);
    }

    private void TrackPending(Guid channelId, string key, Guid optimisticId)
    {
        if (!_pendingByChannel.TryGetValue(channelId, out var map))
        {
            map = new Dictionary<string, Guid>();
            _pendingByChannel[channelId] = map;
        }
        map[key] = optimisticId;
    }

    private void ClearPending(Guid channelId, string key)
    {
        if (_pendingByChannel.TryGetValue(channelId, out var map))
            map.Remove(key);
    }

    private void ReplaceOptimistic(Guid channelId, string key, Guid realId, string content, DateTime createdAt, Guid? parentMessageId)
    {
        if (!_pendingByChannel.TryGetValue(channelId, out var map) || !map.TryGetValue(key, out var optimisticId))
            return;
        map.Remove(key);

        var obs = GetConversation(channelId);
        var next = obs.Value.Select(m => m.Id == optimisticId
            ? m with { Id = realId, Content = content, CreatedAt = createdAt, ParentMessageId = parentMessageId }
            : m).ToList();
        obs.Set(next);
    }

    private void RemoveOptimistic(Guid channelId, Guid optimisticId)
    {
        if (!_byConversation.TryGetValue(channelId, out var obs)) return;
        obs.Set(obs.Value.Where(m => m.Id != optimisticId).ToList());
    }

    private void ScheduleUserPreload(IEnumerable<MockMessage> messages)
    {
        foreach (var authorId in messages.Select(m => m.AuthorId).Distinct())
            _ = _users.EnsureAsync(authorId);
    }

    private static MockMessage ToMockMessage(MessageWireDto m)
        => new(
            m.Id,
            m.AuthorId,
            m.IsDeleted ? string.Empty : m.Content,
            m.CreatedAt,
            IsTombstone: m.IsDeleted,
            IsEdited: m.EditedAt is not null,
            ParentMessageId: m.ParentMessageId,
            ParentPreview: m.ParentPreview is { } p
                ? new MockParentPreview(p.Id, p.AuthorId, p.IsDeleted ? string.Empty : p.Content, p.IsDeleted)
                : null);

    public void Dispose()
    {
        _hub.Reconnected -= OnHubReconnected;
        foreach (var s in _hubSubs) s.Dispose();
        _hubSubs.Clear();
    }

    private sealed class ChannelState
    {
        public bool InitialLoaded;
        public bool LoadingOlder;
        public string? NextCursor;
    }

    private sealed record MessagesPageDto(List<MessageWireDto> Messages, string? NextCursor);

    private sealed record MessageWireDto(
        Guid Id,
        Guid AuthorId,
        string Content,
        Guid? ParentMessageId,
        ParentPreviewWireDto? ParentPreview,
        DateTime CreatedAt,
        DateTime? EditedAt,
        bool IsDeleted,
        List<ReactionWireDto> Reactions);

    private sealed record ParentPreviewWireDto(Guid Id, Guid AuthorId, string Content, bool IsDeleted);
    private sealed record ReactionWireDto(string EmojiCode, int Count, bool ReactedByMe);

    private sealed record SendMessageRequestDto(string Content, string IdempotencyKey, Guid? ParentMessageId);
    private sealed record SendMessageResponseDto(
        Guid MessageId,
        Guid? ChannelId,
        Guid? ConversationId,
        Guid AuthorId,
        string Content,
        Guid? ParentMessageId,
        bool WasIdempotent,
        DateTime CreatedAt);
}
