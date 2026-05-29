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
// Ids must be registered as either a channel (via EnsureChannelLoadedAsync) or a direct conversation (via EnsureConversationLoadedAsync) before the store can talk to the API on their behalf.
public interface IMessageStore
{
    Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId);
    Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default);
    Task EnsureConversationLoadedAsync(Guid conversationId, CancellationToken ct = default);
    Task LoadOlderAsync(Guid conversationId, CancellationToken ct = default);
    bool HasMoreOlder(Guid conversationId);
    Task<Guid?> SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default);
    Task AddReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default);
    Task RemoveReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default);
    Task<MockAttachment> UploadAttachmentAsync(Guid channelId, Guid messageId, string fileName,
        string contentType, long sizeBytes, Stream content, CancellationToken ct = default);
}

public sealed class StubMessageStore : IMessageStore
{
    private readonly Dictionary<Guid, Observable<IReadOnlyList<MockMessage>>> _byConversation = new();

    public Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId)
    {
        if (!_byConversation.TryGetValue(conversationId, out var obs))
        {
            obs = new Observable<IReadOnlyList<MockMessage>>(Array.Empty<MockMessage>());
            _byConversation[conversationId] = obs;
        }
        return obs;
    }

    public Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default) => Task.CompletedTask;
    public Task EnsureConversationLoadedAsync(Guid conversationId, CancellationToken ct = default) => Task.CompletedTask;
    public Task LoadOlderAsync(Guid channelId, CancellationToken ct = default) => Task.CompletedTask;
    public bool HasMoreOlder(Guid channelId) => false;

    public Task<Guid?> SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default)
    {
        var obs = GetConversation(conversationId);
        var optimistic = new MockMessage(Guid.NewGuid(), Guid.Empty, content,
            DateTime.UtcNow, ParentMessageId: parentMessageId);
        obs.Set(obs.Value.Append(optimistic).ToList());
        return Task.FromResult<Guid?>(optimistic.Id);
    }

    public Task AddReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default) => Task.CompletedTask;
    public Task<MockAttachment> UploadAttachmentAsync(Guid channelId, Guid messageId, string fileName,
        string contentType, long sizeBytes, Stream content, CancellationToken ct = default)
        => Task.FromResult(new MockAttachment(Guid.NewGuid(), fileName, contentType, sizeBytes, "#"));
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
    private readonly Dictionary<Guid, TopicState> _topics = new();
    // Pending optimistic sends keyed by idempotency key, scoped per topic (channel or DM), so the 201 echo can swap the placeholder Id without duplicating the row.
    private readonly Dictionary<Guid, Dictionary<string, Guid>> _pendingByTopic = new();

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
            obs = new Observable<IReadOnlyList<MockMessage>>(Array.Empty<MockMessage>());
            _byConversation[conversationId] = obs;
        }
        return obs;
    }

    public Task EnsureLoadedAsync(Guid channelId, CancellationToken ct = default)
        => EnsureLoadedAsync(channelId, TopicKind.Channel, ct);

    public Task EnsureConversationLoadedAsync(Guid conversationId, CancellationToken ct = default)
        => EnsureLoadedAsync(conversationId, TopicKind.Conversation, ct);

    private async Task EnsureLoadedAsync(Guid topicId, TopicKind kind, CancellationToken ct)
    {
        EnsureHubSubscribed();

        if (_topics.TryGetValue(topicId, out var state) && state.InitialLoaded)
            return;

        state ??= new TopicState(kind);
        _topics[topicId] = state;

        var obs = GetConversation(topicId);
        try
        {
            var page = await FetchPageAsync(topicId, kind, cursor: null, ct);
            state.NextCursor = page.NextCursor;
            state.InitialLoaded = true;

            var mapped = page.Messages.Select(ToMockMessage).ToList();
            obs.Set(mapped);
            ScheduleUserPreload(mapped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load messages for {Kind} {TopicId}.", kind, topicId);
            throw;
        }
    }

    public async Task LoadOlderAsync(Guid topicId, CancellationToken ct = default)
    {
        if (!_topics.TryGetValue(topicId, out var state) || !state.InitialLoaded) return;
        if (state.NextCursor is null || state.LoadingOlder) return;

        state.LoadingOlder = true;
        try
        {
            var page = await FetchPageAsync(topicId, state.Kind, state.NextCursor, ct);
            state.NextCursor = page.NextCursor;

            if (page.Messages.Count == 0) return;
            var obs = GetConversation(topicId);
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

    public bool HasMoreOlder(Guid topicId)
        => _topics.TryGetValue(topicId, out var s) && s.InitialLoaded && s.NextCursor is not null;

    public async Task<Guid?> SendAsync(Guid conversationId, string content, Guid? parentMessageId = null,
        CancellationToken ct = default)
    {
        if (!_topics.TryGetValue(conversationId, out var topic))
            throw new InvalidOperationException($"Conversation {conversationId} must be loaded before sending.");

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
                MessagesUrl(conversationId, topic.Kind),
                new SendMessageRequestDto(content, idempotencyKey, parentMessageId),
                ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<SendMessageResponseDto>(cancellationToken: ct);
            if (body is not null)
                ReplaceOptimistic(conversationId, idempotencyKey, body.MessageId, body.Content, body.CreatedAt, body.ParentMessageId);
            return body?.MessageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Send to {Kind} {TopicId} failed; rolling back optimistic message.", topic.Kind, conversationId);
            RemoveOptimistic(conversationId, optimisticId);
            ClearPending(conversationId, idempotencyKey);
            throw;
        }
    }

    public async Task AddReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default)
    {
        // No optimistic update, the SignalR ReactionChanged echo fans the aggregated count back to us. Latency is acceptable for the demo and keeps state authoritative on the server.
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/{channelId}/messages/{messageId}/reactions",
            new AddReactionRequestDto(emojiCode), ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RemoveReactionAsync(Guid channelId, Guid messageId, string emojiCode, CancellationToken ct = default)
    {
        var encoded = WebUtility.UrlEncode(emojiCode);
        using var resp = await _http.DeleteAsync(
            $"api/v1/channels/{channelId}/messages/{messageId}/reactions/{encoded}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<MockAttachment> UploadAttachmentAsync(Guid channelId, Guid messageId, string fileName,
        string contentType, long sizeBytes, Stream content, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);

        using var resp = await _http.PostAsync(
            $"api/v1/channels/{channelId}/messages/{messageId}/attachments", form, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<AttachmentResponseDto>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Empty attachment upload response.");

        var attachment = new MockAttachment(body.AttachmentId, body.FileName, body.ContentType, body.SizeBytes, body.CdnUrl);

        // Locally augment the just-sent message so the uploader sees the attachment immediately. Other clients see it on next history load.
        if (_byConversation.TryGetValue(channelId, out var obs))
        {
            var next = obs.Value.Select(m =>
            {
                if (m.Id != messageId) return m;
                var list = m.Attachments is null ? new List<MockAttachment>() : new List<MockAttachment>(m.Attachments);
                list.Add(attachment);
                return m with { Attachments = list };
            }).ToList();
            obs.Set(next);
        }

        return attachment;
    }

    private void OnReactionChanged(ReactionChangedEvent evt)
    {
        if (evt.ChannelId is not { } channelId) return;
        if (!_byConversation.TryGetValue(channelId, out var obs)) return;

        var meId = _auth.CurrentUser?.Id ?? Guid.Empty;
        var isAdd = string.Equals(evt.Action, "added", StringComparison.OrdinalIgnoreCase);
        var byMe = evt.UserId == meId;

        var next = obs.Value.Select(m =>
        {
            if (m.Id != evt.MessageId) return m;

            var current = m.Reactions is null ? new List<MockReaction>() : new List<MockReaction>(m.Reactions);
            var idx = current.FindIndex(r => r.Emoji == evt.EmojiCode);

            if (isAdd)
            {
                if (idx < 0)
                    current.Add(new MockReaction(evt.EmojiCode, 1, byMe));
                else
                    current[idx] = current[idx] with
                    {
                        Count = current[idx].Count + 1,
                        ReactedByMe = current[idx].ReactedByMe || byMe,
                    };
            }
            else
            {
                if (idx >= 0)
                {
                    var newCount = current[idx].Count - 1;
                    if (newCount <= 0)
                        current.RemoveAt(idx);
                    else
                        current[idx] = current[idx] with
                        {
                            Count = newCount,
                            ReactedByMe = current[idx].ReactedByMe && !byMe,
                        };
                }
            }

            return m with { Reactions = current.Count > 0 ? current : null };
        }).ToList();
        obs.Set(next);
    }

    private void EnsureHubSubscribed()
    {
        if (_hubSubscribed) return;
        _hubSubscribed = true;

        _hubSubs.Add(_hub.OnMessageCreated(OnMessageCreated));
        _hubSubs.Add(_hub.OnMessageEdited(OnMessageEdited));
        _hubSubs.Add(_hub.OnMessageDeleted(OnMessageDeleted));
        _hubSubs.Add(_hub.OnReactionChanged(OnReactionChanged));
    }

    private void OnMessageCreated(MessageCreatedEvent evt)
    {
        var topicId = evt.ChannelId ?? evt.ConversationId;
        if (topicId is not { } id) return;
        if (!_topics.ContainsKey(id)) return;

        var obs = GetConversation(id);
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
        var topicId = evt.ChannelId ?? evt.ConversationId;
        if (topicId is not { } id) return;
        if (!_byConversation.TryGetValue(id, out var obs)) return;
        var next = obs.Value.Select(m => m.Id == evt.MessageId
            ? m with { Content = evt.Content, IsEdited = true }
            : m).ToList();
        obs.Set(next);
    }

    private void OnMessageDeleted(MessageDeletedEvent evt)
    {
        var topicId = evt.ChannelId ?? evt.ConversationId;
        if (topicId is not { } id) return;
        if (!_byConversation.TryGetValue(id, out var obs)) return;
        var next = evt.IsTombstone
            ? obs.Value.Select(m => m.Id == evt.MessageId
                ? m with { Content = string.Empty, IsTombstone = true }
                : m).ToList()
            : obs.Value.Where(m => m.Id != evt.MessageId).ToList();
        obs.Set(next);
    }

    private void OnHubReconnected()
    {
        // On reconnect, refetch every loaded topic from the cursor head. Simpler than reasoning about gap recovery for the small page sizes this client uses.
        foreach (var (topicId, state) in _topics.ToList())
        {
            var kind = state.Kind;
            _topics[topicId] = new TopicState(kind);
            _ = EnsureLoadedAsync(topicId, kind, CancellationToken.None);
        }
    }

    private static string MessagesUrl(Guid id, TopicKind kind) => kind switch
    {
        TopicKind.Channel => $"api/v1/channels/{id}/messages",
        TopicKind.Conversation => $"api/v1/conversations/{id}/messages",
        _ => throw new InvalidOperationException($"Unknown topic kind: {kind}"),
    };

    private async Task<MessagesPageDto> FetchPageAsync(Guid topicId, TopicKind kind, string? cursor, CancellationToken ct)
    {
        var baseUrl = MessagesUrl(topicId, kind);
        var url = cursor is null
            ? $"{baseUrl}?pageSize={PageSize}"
            : $"{baseUrl}?pageSize={PageSize}&cursor={WebUtility.UrlEncode(cursor)}";
        return await _http.GetFromJsonAsync<MessagesPageDto>(url, ct)
               ?? new MessagesPageDto(new List<MessageWireDto>(), null);
    }

    private void TrackPending(Guid topicId, string key, Guid optimisticId)
    {
        if (!_pendingByTopic.TryGetValue(topicId, out var map))
        {
            map = new Dictionary<string, Guid>();
            _pendingByTopic[topicId] = map;
        }
        map[key] = optimisticId;
    }

    private void ClearPending(Guid topicId, string key)
    {
        if (_pendingByTopic.TryGetValue(topicId, out var map))
            map.Remove(key);
    }

    private void ReplaceOptimistic(Guid topicId, string key, Guid realId, string content, DateTime createdAt, Guid? parentMessageId)
    {
        if (!_pendingByTopic.TryGetValue(topicId, out var map) || !map.TryGetValue(key, out var optimisticId))
            return;
        map.Remove(key);

        var obs = GetConversation(topicId);
        var next = obs.Value.Select(m => m.Id == optimisticId
            ? m with { Id = realId, Content = content, CreatedAt = createdAt, ParentMessageId = parentMessageId }
            : m).ToList();
        obs.Set(next);
    }

    private void RemoveOptimistic(Guid topicId, Guid optimisticId)
    {
        if (!_byConversation.TryGetValue(topicId, out var obs)) return;
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
                : null,
            Reactions: m.Reactions is { Count: > 0 }
                ? m.Reactions.Select(r => new MockReaction(r.EmojiCode, r.Count, r.ReactedByMe)).ToList()
                : null,
            Attachments: m.Attachments is { Count: > 0 }
                ? m.Attachments.Select(a => new MockAttachment(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CdnUrl)).ToList()
                : null);

    public void Dispose()
    {
        _hub.Reconnected -= OnHubReconnected;
        foreach (var s in _hubSubs) s.Dispose();
        _hubSubs.Clear();
    }

    private enum TopicKind { Channel, Conversation }

    private sealed class TopicState
    {
        public TopicState(TopicKind kind) => Kind = kind;
        public TopicKind Kind { get; }
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
        List<ReactionWireDto> Reactions,
        List<AttachmentWireDto>? Attachments);

    private sealed record ParentPreviewWireDto(Guid Id, Guid AuthorId, string Content, bool IsDeleted);
    private sealed record ReactionWireDto(string EmojiCode, int Count, bool ReactedByMe);
    private sealed record AttachmentWireDto(Guid Id, string FileName, string ContentType, long SizeBytes, string CdnUrl);

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

    private sealed record AddReactionRequestDto(string EmojiCode);
    private sealed record AttachmentResponseDto(Guid AttachmentId, string FileName, string ContentType, long SizeBytes, string CdnUrl);
}
