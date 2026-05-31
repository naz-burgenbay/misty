using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using Misty.Web.Services.Auth;
using Misty.Web.Services.MockData;

namespace Misty.Web.Services.Users;

public interface IUserProfileService
{
    Task UpdateProfileAsync(string displayName, string? bio, CancellationToken ct = default);
    Task UploadAvatarAsync(IBrowserFile file, CancellationToken ct = default);
    Task RemoveAvatarAsync(CancellationToken ct = default);
    Task DeleteAccountAsync(CancellationToken ct = default);
}

public sealed class HttpUserProfileService : IUserProfileService
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly IAuthService _auth;

    public HttpUserProfileService(HttpClient http, IAuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    public async Task UpdateProfileAsync(string displayName, string? bio, CancellationToken ct = default)
    {
        var me = _auth.CurrentUser ?? throw new InvalidOperationException("Not signed in.");

        using var resp = await _http.PutAsJsonAsync("api/v1/users/me",
            new UpdateProfileRequestDto(displayName, bio, me.Version), ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("Your profile was changed in another session. Reload and try again.");
        await EnsureSuccessAsync(resp, ct);

        var body = await resp.Content.ReadFromJsonAsync<UpdateProfileResponseDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty profile update response.");

        _auth.UpdateCurrentUser(me with
        {
            DisplayName = body.DisplayName,
            Bio = body.Bio,
            AvatarUrl = body.AvatarUrl,
            Version = body.Version,
        });
    }

    public async Task UploadAvatarAsync(IBrowserFile file, CancellationToken ct = default)
    {
        var me = _auth.CurrentUser ?? throw new InvalidOperationException("Not signed in.");

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(file.OpenReadStream(MaxAvatarBytes, ct));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(streamContent, "file", file.Name);

        using var resp = await _http.PostAsync("api/v1/users/me/avatar", content, ct);
        await EnsureSuccessAsync(resp, ct);

        var body = await resp.Content.ReadFromJsonAsync<UploadAvatarResponseDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty avatar upload response.");

        _auth.UpdateCurrentUser(me with
        {
            AvatarUrl = body.AvatarUrl,
            Version = body.Version,
        });
    }

    public async Task RemoveAvatarAsync(CancellationToken ct = default)
    {
        var me = _auth.CurrentUser ?? throw new InvalidOperationException("Not signed in.");

        using var resp = await _http.DeleteAsync("api/v1/users/me/avatar", ct);
        await EnsureSuccessAsync(resp, ct);

        var body = await resp.Content.ReadFromJsonAsync<RemoveAvatarResponseDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty avatar remove response.");

        _auth.UpdateCurrentUser(me with
        {
            AvatarUrl = null,
            Version = body.Version,
        });
    }

    public async Task DeleteAccountAsync(CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync("api/v1/users/me", ct);
        await EnsureSuccessAsync(resp, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        string? detail = null;
        try
        {
            var problem = await resp.Content.ReadFromJsonAsync<ProblemDetailsDto>(cancellationToken: ct);
            detail = problem?.Detail ?? problem?.Title;
        }
        catch
        {
            try { detail = await resp.Content.ReadAsStringAsync(ct); } catch { }
        }
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Request failed ({(int)resp.StatusCode} {resp.ReasonPhrase})."
            : $"{(int)resp.StatusCode}: {Truncate(detail, 240)}";
        throw new InvalidOperationException(message);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\u2026";

    private sealed record UpdateProfileRequestDto(string DisplayName, string? Bio, string Version);
    private sealed record UpdateProfileResponseDto(Guid UserId, string Username, string Email, string DisplayName, string? Bio, string? AvatarUrl, string Version);
    private sealed record UploadAvatarResponseDto(string AvatarUrl, string Version);
    private sealed record RemoveAvatarResponseDto(string Version);
    private sealed record ProblemDetailsDto(string? Title, string? Detail, int? Status);
}
