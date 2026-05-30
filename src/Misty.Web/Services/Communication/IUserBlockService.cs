namespace Misty.Web.Services.Communication;

public interface IUserBlockService
{
    Task BlockAsync(Guid userId, CancellationToken ct = default);
    Task UnblockAsync(Guid userId, CancellationToken ct = default);
}

public sealed class HttpUserBlockService : IUserBlockService
{
    private readonly HttpClient _http;

    public HttpUserBlockService(HttpClient http) => _http = http;

    public async Task BlockAsync(Guid userId, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsync($"api/v1/users/{userId}/block", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UnblockAsync(Guid userId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/v1/users/{userId}/block", ct);
        resp.EnsureSuccessStatusCode();
    }
}
