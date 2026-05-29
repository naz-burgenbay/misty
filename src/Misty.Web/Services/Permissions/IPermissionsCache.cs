using Misty.Web.Services.Common;

namespace Misty.Web.Services.Permissions;

// Mirrors the server-side ChannelPermission flags enum
[Flags]
public enum ChannelPermissionFlags : long
{
    None              = 0,
    SendMessages      = 1 << 0,
    ManageMessages    = 1 << 1,
    ManageMembers     = 1 << 2,
    ManageRoles       = 1 << 3,
    ManageChannel     = 1 << 4,
    ModerateMembers   = 1 << 5,
}

// Per-channel permission cache invalidated on MembershipChanged/RoleChanged/ModerationActionApplied SignalR events broadcast by PermissionEventsBroadcastWorker.
// Returns the current user's effective flags for a given channel.
public interface IPermissionsCache
{
    ChannelPermissionFlags Get(Guid channelId);
    Observable<ChannelPermissionFlags> Watch(Guid channelId);
    bool Has(Guid channelId, ChannelPermissionFlags flag);
    void Invalidate(Guid channelId);
}

public sealed class StubPermissionsCache : IPermissionsCache
{
    private readonly Dictionary<Guid, Observable<ChannelPermissionFlags>> _byChannel = new();

    public ChannelPermissionFlags Get(Guid channelId) => Watch(channelId).Value;

    public Observable<ChannelPermissionFlags> Watch(Guid channelId)
    {
        if (!_byChannel.TryGetValue(channelId, out var obs))
        {
            obs = new Observable<ChannelPermissionFlags>(Resolve(channelId));
            _byChannel[channelId] = obs;
        }
        return obs;
    }

    public bool Has(Guid channelId, ChannelPermissionFlags flag) => (Get(channelId) & flag) == flag;

    public void Invalidate(Guid channelId)
    {
        if (_byChannel.TryGetValue(channelId, out var obs))
            obs.Set(Resolve(channelId));
    }

    private static ChannelPermissionFlags Resolve(Guid channelId) => ChannelPermissionFlags.None;
}
