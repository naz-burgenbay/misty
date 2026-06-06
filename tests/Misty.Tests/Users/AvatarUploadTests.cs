using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Users;

[Collection("Integration")]
public sealed class AvatarUploadTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    private static readonly byte[] OnePxPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xA8, 0xE3, 0x35, 0xB2, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public AvatarUploadTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var db = _factory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            SchemasToInclude = ["users"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string accessToken, Guid userId)> RegisterAndLoginAsync(string username)
    {
        await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });

        var body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("userId").GetGuid());
    }

    private static MultipartFormDataContent MakePngContent(byte[]? data = null)
    {
        var bytes = data ?? OnePxPng;
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "avatar.png");
        return form;
    }

    private async Task<string> CurrentUserVersionAsync(Guid userId)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        return Convert.ToBase64String(user.Version);
    }

    private static void AddVersion(MultipartFormDataContent form, string version)
        => form.Add(new StringContent(version), "version");

    [Fact]
    public async Task UploadAvatar_WithValidPng_Returns200AndSetsAvatarUrl()
    {
        var (token, userId) = await RegisterAndLoginAsync("avatar1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = MakePngContent();
        AddVersion(form, await CurrentUserVersionAsync(userId));
        var response = await _client.PostAsync("/api/v1/users/me/avatar", form);

        var debugBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, debugBody);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var avatarUrl = body.GetProperty("avatarUrl").GetString();
        avatarUrl.Should().NotBeNullOrEmpty();
        avatarUrl.Should().Contain(userId.ToString(), "blob is named after the user ID");

        // Profile now reflects the avatarUrl
        var profile = await _client.GetAsync($"/api/v1/users/{userId}");
        var profileBody = await profile.Content.ReadFromJsonAsync<JsonElement>();
        profileBody.GetProperty("avatarUrl").GetString().Should().Be(avatarUrl);
    }

    [Fact]
    public async Task UploadAvatar_Twice_OverwritesPreviousAvatar()
    {
        var (token, userId) = await RegisterAndLoginAsync("avatar2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form1 = MakePngContent();
        AddVersion(form1, await CurrentUserVersionAsync(userId));
        var first = await _client.PostAsync("/api/v1/users/me/avatar", form1);
        var url1 = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avatarUrl").GetString();

        using var form2 = MakePngContent();
        AddVersion(form2, await CurrentUserVersionAsync(userId));
        var second = await _client.PostAsync("/api/v1/users/me/avatar", form2);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var url2 = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avatarUrl").GetString();

        // Both uploads land at the same blob path (userId), so the URL is the same
        url2.Should().Be(url1);
    }

    [Fact]
    public async Task UploadAvatar_WithOversizedFile_Returns400()
    {
        var (token, _) = await RegisterAndLoginAsync("avatar3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 6 MB of zeros exceeds the 5 MB limit
        var oversized = new byte[6 * 1024 * 1024];
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(oversized);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "big.png");
        AddVersion(form, "AAAAAAAAAAA=");

        var response = await _client.PostAsync("/api/v1/users/me/avatar", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadAvatar_WithUnsupportedContentType_Returns400()
    {
        var (token, _) = await RegisterAndLoginAsync("avatar4");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]); // %PDF magic bytes
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "file", "document.pdf");
        AddVersion(form, "AAAAAAAAAAA=");

        var response = await _client.PostAsync("/api/v1/users/me/avatar", form);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
