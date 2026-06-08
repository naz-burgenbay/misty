namespace Misty.Application.Presence;

public interface IPresenceTracker
{
    Task<bool> TrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    Task<bool> UntrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
