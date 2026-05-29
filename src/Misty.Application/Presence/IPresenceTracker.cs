namespace Misty.Application.Presence;

// Tracks live user connections in shared storage so presence state survives across multiple App Service instances. Returns true from track/untrack only when the user's overall online state flipped, so callers know whether to broadcast a PresenceChanged event.
public interface IPresenceTracker
{
    Task<bool> TrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    Task<bool> UntrackConnectionAsync(Guid userId, string connectionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
