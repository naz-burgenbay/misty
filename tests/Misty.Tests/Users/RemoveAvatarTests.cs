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
public sealed class RemoveAvatarTests : IAsyncLifetime
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

    public RemoveAvatarTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
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

    private static MultipartFormDataContent MakePngContent()
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(OnePxPng);
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

    private Task<HttpResponseMessage> SendJsonDeleteAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) };
        return _client.SendAsync(req);
    }

    private async Task<string?> UploadAvatarAsync(string token, Guid userId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var form = MakePngContent();
        form.Add(new StringContent(await CurrentUserVersionAsync(userId)), "version");
        var resp = await _client.PostAsync("/api/v1/users/me/avatar", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("avatarUrl").GetString();
    }

    [Fact]
    public async Task RemoveAvatar_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = "AAAAAAAAAAA=" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveAvatar_AfterUpload_ClearsAvatarUrl_AndReturnsVersion()
    {
        var (token, userId) = await RegisterAndLoginAsync("rmavatar1");
        var uploadedUrl = await UploadAvatarAsync(token, userId);
        uploadedUrl.Should().NotBeNullOrEmpty();

        var resp = await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = await CurrentUserVersionAsync(userId) });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();

        var profile = await _client.GetAsync($"/api/v1/users/{userId}");
        profile.StatusCode.Should().Be(HttpStatusCode.OK);
        var profileBody = await profile.Content.ReadFromJsonAsync<JsonElement>();
        var avatarProp = profileBody.GetProperty("avatarUrl");
        (avatarProp.ValueKind == JsonValueKind.Null || string.IsNullOrEmpty(avatarProp.GetString()))
            .Should().BeTrue("AvatarUrl should be cleared after RemoveAvatar");
    }

    [Fact]
    public async Task RemoveAvatar_WhenNoAvatar_IsIdempotent()
    {
        var (token, userId) = await RegisterAndLoginAsync("rmavatar2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = await CurrentUserVersionAsync(userId) });

        resp.StatusCode.Should().Be(HttpStatusCode.OK, "delete with no existing avatar must succeed");
    }

    [Fact]
    public async Task RemoveAvatar_TwiceInARow_Succeeds()
    {
        var (token, userId) = await RegisterAndLoginAsync("rmavatar3");
        await UploadAvatarAsync(token, userId);

        var first = await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = await CurrentUserVersionAsync(userId) });
        var second = await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = await CurrentUserVersionAsync(userId) });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
