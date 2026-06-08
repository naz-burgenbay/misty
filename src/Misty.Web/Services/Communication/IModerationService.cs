using System.Net.Http.Json;

namespace Misty.Web.Services.Communication;

public enum ModerationActionType
{
    Mute = 0,
    Ban = 1,
    Warn = 2,
}

public sealed record ActiveModerationDto(
    Guid ActionId,
    ModerationActionType Type,
    string Reason,
    string Version);

public interface IModerationService
{
    Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionType type, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default);

    Task<Guid> KickAsync(Guid channelId, Guid userId, string reason, CancellationToken ct = default);

    Task RevokeAsync(Guid channelId, Guid userId, Guid actionId, string version, CancellationToken ct = default);

    Task<IReadOnlyList<ActiveModerationDto>> GetActiveAsync(Guid channelId, Guid userId, CancellationToken ct = default);
}

public sealed class HttpModerationService : IModerationService
{
    private readonly HttpClient _http;

    public HttpModerationService(HttpClient http) => _http = http;

    public async Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionType type, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var body = new ApplyRequestDto((int)type, reason, expiresAt);
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/{channelId}/members/{userId}/moderation", body, ct);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ApplyResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty moderation response.");
        return payload.ActionId;
    }

    public async Task<Guid> KickAsync(Guid channelId, Guid userId, string reason, CancellationToken ct = default)
    {
        var body = new KickRequestDto(reason);
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"api/v1/channels/{channelId}/members/{userId}")
        {
            Content = JsonContent.Create(body),
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ApplyResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty kick response.");
        return payload.ActionId;
    }

    public async Task RevokeAsync(Guid channelId, Guid userId, Guid actionId, string version, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete,
            $"api/v1/channels/{channelId}/members/{userId}/moderation/{actionId}")
        {
            Content = JsonContent.Create(new RevokeRequestDto(version)),
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ActiveModerationDto>> GetActiveAsync(Guid channelId, Guid userId, CancellationToken ct = default)
    {
        var raw = await _http.GetFromJsonAsync<List<GetModerationResponseDto>>(
            $"api/v1/channels/{channelId}/members/{userId}/moderation", ct)
            ?? new List<GetModerationResponseDto>();
        return raw.Select(a => new ActiveModerationDto(a.ActionId, (ModerationActionType)a.Type, a.Reason, a.Version))
                  .ToList();
    }

    private sealed record ApplyRequestDto(int Type, string Reason, DateTime? ExpiresAt);
    private sealed record KickRequestDto(string Reason);
    private sealed record RevokeRequestDto(string Version);
    private sealed record ApplyResponseDto(Guid ActionId);
    private sealed record GetModerationResponseDto(Guid ActionId, int Type, Guid IssuedByUserId, string Reason, DateTime? ExpiresAt, string Version);
}
