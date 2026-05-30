using System.Net.Http.Json;

namespace Misty.Web.Services.Communication;

public sealed record ChannelRoleDto(Guid Id, string Name, long Permissions);

public interface IChannelRolesService
{
    Task<IReadOnlyList<ChannelRoleDto>> GetRolesAsync(Guid channelId, CancellationToken ct = default);
    Task AssignAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default);
    Task RevokeAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default);
}

public sealed class HttpChannelRolesService : IChannelRolesService
{
    private readonly HttpClient _http;

    public HttpChannelRolesService(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ChannelRoleDto>> GetRolesAsync(Guid channelId, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ChannelRoleDto>>(
                       $"api/v1/channels/{channelId}/roles", ct)
                   ?? new List<ChannelRoleDto>();
        return list;
    }

    public async Task AssignAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync(
            $"api/v1/channels/{channelId}/members/{userId}/roles/{roleId}", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task RevokeAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync(
            $"api/v1/channels/{channelId}/members/{userId}/roles/{roleId}", ct);
        resp.EnsureSuccessStatusCode();
    }
}
