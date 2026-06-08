using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using StackExchange.Redis;

namespace Misty.Infrastructure.Communication;

public sealed class CachedPermissionService : IPermissionService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly PermissionService _inner;
    private readonly IDatabase _redis;

    public CachedPermissionService(PermissionService inner, IConnectionMultiplexer mux)
    {
        _inner = inner;
        _redis = mux.GetDatabase();
    }

    public async Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default)
    {
        var effective = await GetEffectiveRawAsync(userId, channelId, ct);

        if (effective == PermissionService.DeniedSentinel)
            return false;

        return ((ChannelPermission)effective & permission) == permission;
    }

    public async Task<ChannelPermission> GetEffectivePermissionsAsync(
        Guid userId,
        Guid channelId,
        CancellationToken ct = default)
    {
        var effective = await GetEffectiveRawAsync(userId, channelId, ct);
        return effective == PermissionService.DeniedSentinel ? ChannelPermission.None : (ChannelPermission)effective;
    }

    private async Task<long> GetEffectiveRawAsync(Guid userId, Guid channelId, CancellationToken ct)
    {
        var key = CacheKey(userId, channelId);
        var cached = await _redis.StringGetAsync(key);

        if (cached.HasValue) return (long)cached;

        var effective = await _inner.ComputeEffectivePermissionsAsync(userId, channelId, ct);
        await _redis.StringSetAsync(key, effective, Ttl);
        return effective;
    }

    public static string CacheKey(Guid userId, Guid channelId) =>
        $"perm:{userId}:{channelId}";
}
