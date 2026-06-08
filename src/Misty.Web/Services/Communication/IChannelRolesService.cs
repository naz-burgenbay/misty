using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Misty.Web.Services.Communication;

public sealed record ChannelRoleDto(
    [property: JsonPropertyName("roleId")] Guid Id,
    Guid ChannelId,
    string Name,
    long Permissions,
    bool IsOwnerRole,
    string Version);

internal sealed record CreateChannelRoleRequestDto(
    string Name,
    long Permissions);

internal sealed record UpdateChannelRoleRequestDto(
    string Name,
    long Permissions,
    string Version);

public interface IChannelRolesService
{
    Task<IReadOnlyList<ChannelRoleDto>> GetRolesAsync(Guid channelId, CancellationToken ct = default);
    Task AssignAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default);
    Task RevokeAsync(Guid channelId, Guid userId, Guid roleId, CancellationToken ct = default);
    Task<ChannelRoleDto> CreateAsync(Guid channelId, string name, long permissions, CancellationToken ct = default);
    Task<ChannelRoleDto> UpdateAsync(Guid channelId, Guid roleId, string name, long permissions, string version, CancellationToken ct = default);
    Task DeleteAsync(Guid channelId, Guid roleId, CancellationToken ct = default);
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

    public async Task<ChannelRoleDto> CreateAsync(Guid channelId, string name, long permissions, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/{channelId}/roles",
            new CreateChannelRoleRequestDto(name, permissions), ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ChannelRoleDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty create role response.");
    }

    public async Task<ChannelRoleDto> UpdateAsync(Guid channelId, Guid roleId, string name, long permissions, string version, CancellationToken ct = default)
    {
        using var resp = await _http.PutAsJsonAsync(
            $"api/v1/channels/{channelId}/roles/{roleId}",
            new UpdateChannelRoleRequestDto(name, permissions, version), ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ChannelRoleDto>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty update role response.");
    }

    public async Task DeleteAsync(Guid channelId, Guid roleId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync(
            $"api/v1/channels/{channelId}/roles/{roleId}", ct);
        resp.EnsureSuccessStatusCode();
    }
}
