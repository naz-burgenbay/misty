using Misty.Domain.Communication;

namespace Misty.Application.Communication.Contracts;

public interface IPermissionService
{
    Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default);

    Task<ChannelPermission> GetEffectivePermissionsAsync(
        Guid userId,
        Guid channelId,
        CancellationToken ct = default);
}
