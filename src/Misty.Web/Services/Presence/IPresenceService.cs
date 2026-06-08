using Misty.Web.Services.Common;

namespace Misty.Web.Services.Presence;

public enum PresenceState { Unknown, Online, Idle, Offline }

public interface IPresenceService
{
    PresenceState Get(Guid userId);
    Observable<PresenceState> Watch(Guid userId);
}

public sealed class StubPresenceService : IPresenceService
{
    private readonly Dictionary<Guid, Observable<PresenceState>> _byUser = new();

    public PresenceState Get(Guid userId) => PresenceState.Online;

    public Observable<PresenceState> Watch(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var obs))
        {
            obs = new Observable<PresenceState>(PresenceState.Online);
            _byUser[userId] = obs;
        }
        return obs;
    }
}
