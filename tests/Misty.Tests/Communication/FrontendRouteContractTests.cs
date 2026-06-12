using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Presence;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class FrontendRouteContractTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public FrontendRouteContractTests(ApiFactory factory)
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
            SchemasToInclude = ["users", "comm"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            Password = "Str0ngPass!",
        });
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private void UseToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> CreateChannelAsync(string ownerToken, string name, long defaultPermissions = 0L)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = defaultPermissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task JoinAsync(string memberToken, Guid channelId)
    {
        UseToken(memberToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetChannelPermissionsMe_OwnerReturnsAdminFlags()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("frc_perm_owner");
        var channelId = await CreateChannelAsync(ownerToken, "frc-perm-ch");

        UseToken(ownerToken);
        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/permissions/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channelId").GetGuid().Should().Be(channelId);

        var effective = (ChannelPermission)body.GetProperty("effectivePermissions").GetInt64();
        effective.HasFlag(ChannelPermission.ManageChannel).Should().BeTrue();
        effective.HasFlag(ChannelPermission.ManageRoles).Should().BeTrue();
        effective.HasFlag(ChannelPermission.ManageMembers).Should().BeTrue();
    }

    [Fact]
    public async Task GetChannelPermissionsMe_PlainMemberWithoutRolesReturnsNone()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("frc_perm_owner2");
        var (memberToken, _) = await RegisterAndLoginAsync("frc_perm_member2");
        var channelId = await CreateChannelAsync(ownerToken, "frc-perm-ch2");
        await JoinAsync(memberToken, channelId);

        UseToken(memberToken);
        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/permissions/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channelId").GetGuid().Should().Be(channelId);
        ((ChannelPermission)body.GetProperty("effectivePermissions").GetInt64())
            .Should().Be(ChannelPermission.None,
                "a member with no assigned roles has no effective permissions in the current model");
    }

    [Fact]
    public async Task GetChannelPermissionsMe_NonMemberReturnsNone()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("frc_perm_owner3");
        var (outsiderToken, _) = await RegisterAndLoginAsync("frc_perm_outsider");
        var channelId = await CreateChannelAsync(ownerToken, "frc-perm-ch3");

        UseToken(outsiderToken);
        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/permissions/me");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        ((ChannelPermission)body.GetProperty("effectivePermissions").GetInt64())
            .Should().Be(ChannelPermission.None);
    }

    [Fact]
    public async Task PostPresenceBulk_ReturnsIsOnlinePerUser()
    {
        var (tokenA, userIdA) = await RegisterAndLoginAsync("frc_pres_a");
        var (_, userIdB) = await RegisterAndLoginAsync("frc_pres_b");

        using (var scope = _factory.Services.CreateScope())
        {
            var tracker = scope.ServiceProvider.GetRequiredService<IPresenceTracker>();
            await tracker.TrackConnectionAsync(userIdA, "test-conn-A");
        }

        try
        {
            UseToken(tokenA);
            var resp = await _client.PostAsJsonAsync("/api/v1/presence/bulk", new
            {
                userIds = new[] { userIdA, userIdB },
            });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var statuses = body.GetProperty("statuses").EnumerateArray()
                .ToDictionary(s => s.GetProperty("userId").GetGuid(),
                              s => s.GetProperty("isOnline").GetBoolean());

            statuses.Should().ContainKey(userIdA);
            statuses.Should().ContainKey(userIdB);
            statuses[userIdA].Should().BeTrue("user A has a tracked connection");
            statuses[userIdB].Should().BeFalse("user B has never connected");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IPresenceTracker>();
            await tracker.UntrackConnectionAsync(userIdA, "test-conn-A");
        }
    }

    [Fact]
    public async Task PostPresenceBulk_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.PostAsJsonAsync("/api/v1/presence/bulk", new
        {
            userIds = new[] { Guid.NewGuid() },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
