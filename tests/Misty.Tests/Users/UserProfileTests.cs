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
public sealed class UserProfileTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public UserProfileTests(ApiFactory factory)
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

    private async Task<(string accessToken, string refreshToken, Guid userId)> RegisterAndLoginAsync(string username)
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
        return (
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!,
            body.GetProperty("userId").GetGuid()
        );
    }

    [Fact]
    public async Task GetUser_WithValidId_ReturnsProfile()
    {
        var (token, _, userId) = await RegisterAndLoginAsync("getprofile");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/api/v1/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("userId").GetGuid().Should().Be(userId);
        body.GetProperty("username").GetString().Should().Be("getprofile");
        body.GetProperty("displayName").GetString().Should().Be("getprofile Display");
        body.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetUser_WithUnknownId_Returns404()
    {
        var (token, _, _) = await RegisterAndLoginAsync("getempty");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateProfile_WithValidVersion_UpdatesFields()
    {
        var (token, _, userId) = await RegisterAndLoginAsync("updateme");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var getResp = await _client.GetAsync($"/api/v1/users/{userId}");
        var profile = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var version = profile.GetProperty("version").GetString()!;

        var putResp = await _client.PutAsJsonAsync("/api/v1/users/me", new
        {
            DisplayName = "Updated Name",
            Bio = "My bio",
            Version = version,
        });

        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await putResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("displayName").GetString().Should().Be("Updated Name");
        updated.GetProperty("bio").GetString().Should().Be("My bio");
        updated.GetProperty("version").GetString().Should().NotBe(version, "rowversion must change after update");
    }

    [Fact]
    public async Task UpdateProfile_WithStaleVersion_Returns409()
    {
        var (token, _, userId) = await RegisterAndLoginAsync("stalever");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var getResp = await _client.GetAsync($"/api/v1/users/{userId}");
        var profile = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        var originalVersion = profile.GetProperty("version").GetString()!;

        await _client.PutAsJsonAsync("/api/v1/users/me", new
        {
            DisplayName = "First Update",
            Bio = (string?)null,
            Version = originalVersion,
        });

        var conflictResp = await _client.PutAsJsonAsync("/api/v1/users/me", new
        {
            DisplayName = "Second Update",
            Bio = (string?)null,
            Version = originalVersion,
        });

        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteUser_Returns204_AndRevokesRefreshTokens()
    {
        var (token, refreshToken, _) = await RegisterAndLoginAsync("deleteme");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deleteResp = await _client.DeleteAsync("/api/v1/users/me");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;
        var refreshResp = await _client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteUser_ProfileUnavailableAfterDelete()
    {
        var (token, _, userId) = await RegisterAndLoginAsync("deleteme2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _client.DeleteAsync("/api/v1/users/me");

        var getResp = await _client.GetAsync($"/api/v1/users/{userId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
