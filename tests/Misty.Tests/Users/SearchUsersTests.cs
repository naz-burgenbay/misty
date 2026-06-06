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
public sealed class SearchUsersTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public SearchUsersTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username, string? displayName = null)
    {
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = displayName ?? $"{username} Display",
            Password = "Str0ngPass!",
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var loginBody = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Search_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.GetAsync("/api/v1/users/search?q=alice");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Search_ByUsernameSubstring_ReturnsMatch_AndExcludesCaller()
    {
        var (callerToken, _) = await RegisterAndLoginAsync("srch_caller_alpha");
        var (_, targetId) = await RegisterAndLoginAsync("srch_target_alpha");

        SetToken(callerToken);
        var resp = await _client.GetAsync("/api/v1/users/search?q=srch_target");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var results = body.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCount(1);
        results[0].GetProperty("userId").GetGuid().Should().Be(targetId);
        results[0].GetProperty("username").GetString().Should().Be("srch_target_alpha");
    }

    [Fact]
    public async Task Search_ByDisplayNameSubstring_ReturnsMatch()
    {
        var (callerToken, _) = await RegisterAndLoginAsync("srch_caller_beta");
        var (_, targetId) = await RegisterAndLoginAsync("srch_disp_beta", displayName: "Wonderful Penguin");

        SetToken(callerToken);
        var resp = await _client.GetAsync("/api/v1/users/search?q=Penguin");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("userId").GetGuid()).ToList();
        ids.Should().Contain(targetId);
    }

    [Fact]
    public async Task Search_ExcludesCaller_EvenWhenCallerMatchesQuery()
    {
        var (callerToken, callerId) = await RegisterAndLoginAsync("srch_self_gamma");

        SetToken(callerToken);
        var resp = await _client.GetAsync("/api/v1/users/search?q=srch_self_gamma");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("userId").GetGuid()).ToList();
        ids.Should().NotContain(callerId);
    }

    [Fact]
    public async Task Search_TakeIsClampedTo20()
    {
        var (callerToken, _) = await RegisterAndLoginAsync("srch_take_caller");

        for (var i = 0; i < 25; i++)
            await RegisterAndLoginAsync($"srch_take_target{i:00}");

        SetToken(callerToken);
        var resp = await _client.GetAsync("/api/v1/users/search?q=srch_take_target&take=100");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var results = body.GetProperty("results").EnumerateArray().ToList();
        results.Should().HaveCountLessThanOrEqualTo(20, "the handler clamps take to a maximum of 20");
    }

    [Fact]
    public async Task Search_DoesNotReturnSoftDeletedUsers()
    {
        var (callerToken, _) = await RegisterAndLoginAsync("srch_sd_caller");
        var (deletedToken, deletedId) = await RegisterAndLoginAsync("srch_sd_target_delta");

        SetToken(deletedToken);
        (await _client.DeleteAsync("/api/v1/users/me")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        SetToken(callerToken);
        var resp = await _client.GetAsync("/api/v1/users/search?q=srch_sd_target");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("userId").GetGuid()).ToList();
        ids.Should().NotContain(deletedId, "soft-deleted users must be excluded from search results");
    }
}
