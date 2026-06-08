using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Users;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Users;

[Collection("Integration")]
public sealed class RefreshTokenTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public RefreshTokenTests(ApiFactory factory)
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
            DisplayName = $"{username} User",
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
    public async Task Refresh_WithValidToken_ReturnsNewTokenPair()
    {
        var (originalAccess, originalRefresh, _) = await RegisterAndLoginAsync("refreshuser");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = originalRefresh });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newAccess = body.GetProperty("accessToken").GetString();
        var newRefresh = body.GetProperty("refreshToken").GetString();

        newAccess.Should().NotBeNullOrEmpty();
        newRefresh.Should().NotBeNullOrEmpty();
        newRefresh.Should().NotBe(originalRefresh, "rotation must issue a new refresh token");
    }

    [Fact]
    public async Task Refresh_WithUsedToken_Returns401_ReplayAttack()
    {
        var (_, refreshToken, _) = await RegisterAndLoginAsync("replayuser");

        var firstResp = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayResp = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        replayResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithRevokedToken_Returns401()
    {
        var (_, refreshToken, userId) = await RegisterAndLoginAsync("revokeduser");

        await using var db = _factory.CreateDbContext();
        var rt = await db.RefreshTokens.FirstAsync(t => t.UserId == userId);
        rt.Revoke();
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Returns401()
    {
        var (_, refreshToken, userId) = await RegisterAndLoginAsync("expireduser");

        await using var db = _factory.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [users].[RefreshToken] SET ExpiresAt = {DateTime.UtcNow.AddHours(-1)} WHERE UserId = {userId}");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_SubsequentRefreshReturns401()
    {
        var (_, refreshToken, _) = await RegisterAndLoginAsync("logoutuser");

        var logoutResp = await _client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = refreshToken });
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshResp = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });
        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}