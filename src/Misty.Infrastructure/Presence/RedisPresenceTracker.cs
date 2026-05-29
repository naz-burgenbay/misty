using Misty.Application.Presence;
using StackExchange.Redis;

namespace Misty.Infrastructure.Presence;

// Redis-backed presence: each user has a SET keyed presence:user:{userId} containing their live SignalR connection ids. The user is online iff that set is non-empty. A transition is reported only when the set size crosses 0, so duplicate events aren't fanned out for each additional tab the user opens.
public sealed class RedisPresenceTracker : IPresenceTracker
{
    private readonly IConnectionMultiplexer _redis;

    public RedisPresenceTracker(IConnectionMultiplexer redis) => _redis = redis;

    private static RedisKey Key(Guid userId) => $"presence:user:{userId}";

    public async Task<bool> TrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var added = await db.SetAddAsync(Key(userId), connectionId);
        if (!added) return false;
        var size = await db.SetLengthAsync(Key(userId));
        return size == 1;
    }

    public async Task<bool> UntrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var removed = await db.SetRemoveAsync(Key(userId), connectionId);
        if (!removed) return false;
        var size = await db.SetLengthAsync(Key(userId));
        return size == 0;
    }

    public async Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, bool>();

        var db = _redis.GetDatabase();
        var batch = db.CreateBatch();
        var pending = userIds.Select(id => (id, task: batch.SetLengthAsync(Key(id)))).ToList();
        batch.Execute();

        var result = new Dictionary<Guid, bool>(pending.Count);
        foreach (var (id, task) in pending)
        {
            var size = await task;
            result[id] = size > 0;
        }
        return result;
    }
}
