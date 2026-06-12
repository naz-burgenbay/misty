using System.Net.Http.Json;

namespace Misty.Web.Services.Communication;

public sealed class HttpReportService : IReportService
{
    private readonly HttpClient _http;

    public HttpReportService(HttpClient http) => _http = http;

    public async Task SubmitAsync(string targetKind, Guid targetId, string reason, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            "api/v1/reports",
            new { targetKind, targetId, reason },
            ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ReportItemDto>> GetPendingAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<List<ReportItemDto>>(
            $"api/v1/reports?skip={skip}&take={take}", ct);
        return result ?? [];
    }

    public async Task ReviewAsync(Guid reportId, bool approve, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/reports/{reportId}/review",
            new { approve },
            ct);
        resp.EnsureSuccessStatusCode();
    }
}
