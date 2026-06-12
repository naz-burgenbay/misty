using System.Net;
using System.Net.Http.Json;

namespace Misty.Web.Services.Common;

public static class HttpErrors
{
    public static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException hre => hre.StatusCode switch
        {
            HttpStatusCode.Conflict => "This action couldn't be completed due to a conflict. Please refresh and try again.",
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "The requested resource was not found.",
            HttpStatusCode.BadRequest => "The request was invalid. Please check your input.",
            HttpStatusCode.TooManyRequests => "You're doing that too fast. Please wait a moment and try again.",
            HttpStatusCode.InternalServerError => "Something went wrong on the server. Please try again later.",
            HttpStatusCode.ServiceUnavailable => "The service is temporarily unavailable. Please try again later.",
            _ => "Something went wrong. Please try again.",
        },
        InvalidOperationException ioe when IsStatusMessage(ioe.Message) => MapStatusMessage(ioe.Message),
        _ => "Something went wrong. Please try again.",
    };

    public static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct = default)
    {
        if (resp.IsSuccessStatusCode) return;
        string? detail = null;
        try
        {
            var problem = await resp.Content.ReadFromJsonAsync<ProblemDto>(cancellationToken: ct);
            detail = problem?.Detail ?? problem?.Title;
        }
        catch
        {
            try { detail = await resp.Content.ReadAsStringAsync(ct); } catch { }
        }
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Request failed ({(int)resp.StatusCode})."
            : Truncate(detail, 240);
        throw new InvalidOperationException(message);
    }

    private static bool IsStatusMessage(string msg) =>
        msg.StartsWith("Request failed (", StringComparison.Ordinal) ||
        msg.Contains("409") || msg.Contains("403") || msg.Contains("404");

    private static string MapStatusMessage(string msg)
    {
        if (msg.Contains("409")) return "This action couldn't be completed due to a conflict. Please refresh and try again.";
        if (msg.Contains("403")) return "You don't have permission to do that.";
        if (msg.Contains("404")) return "The requested resource was not found.";
        return "Something went wrong. Please try again.";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record ProblemDto(string? Title, string? Detail, int? Status);
}
