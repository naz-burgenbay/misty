using System.Net.Http.Json;

namespace Misty.Web.Services.Communication;

public enum ModerationActionKind
{
    Mute = 0,
    Ban = 1,
    Warn = 2,
}

public interface IModerationService
{
    Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionKind kind, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default);

    Task<Guid> KickAsync(Guid channelId, Guid userId, string reason, CancellationToken ct = default);
}

public sealed class HttpModerationService : IModerationService
{
    private readonly HttpClient _http;

    public HttpModerationService(HttpClient http) => _http = http;

    public async Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionKind kind, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var body = new ApplyRequestDto((int)kind, reason, expiresAt);
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

    private sealed record ApplyRequestDto(int Type, string Reason, DateTime? ExpiresAt);
    private sealed record KickRequestDto(string Reason);
    private sealed record ApplyResponseDto(Guid ActionId);
}
