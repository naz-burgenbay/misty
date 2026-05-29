using Misty.Web.Services.Common;
using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Messaging;

// Per-conversation observable message list. Optimistic insert produces a local message with a client-generated idempotency key; when the SignalR MessageCreated event arrives (or the 201 response with the persisted Id), the optimistic entry is replaced atomically using the key (SignalR-vs-201 dedup as described in the design system).
public interface IMessageStore
{
    Observable<IReadOnlyList<MockMessage>> GetConversation(Guid conversationId);
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
