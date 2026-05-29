using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Infrastructure.Communication;
using Misty.Tests.Integration;
using Respawn;
using StackExchange.Redis;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class CacheInvalidationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public CacheInvalidationTests(ApiFactory factory)
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

        // Flush all Redis keys between tests so cached state is clean.
        var mux = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var server = mux.GetServer(mux.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
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

    private async Task<Guid> CreateChannelAsync(string ownerToken, string name)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task JoinChannelAsync(string memberToken, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateRoleAsync(string ownerToken, Guid channelId, ChannelPermission permissions)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "TestRole",
            Permissions = (long)permissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    private async Task AssignRoleAsync(string ownerToken, Guid channelId, Guid memberId, Guid roleId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task RevokeRoleAsync(string ownerToken, Guid channelId, Guid memberId, Guid roleId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.DeleteAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task FullInvalidationCycle_RoleRevocation_ClearsPermissionCache()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cachetest_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("cachetest_member");

        var channelId = await CreateChannelAsync(ownerToken, "CacheTestChannel");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Wait for setup-time cache invalidation events to finish processing before warming the cache, otherwise a delayed RoleChanged event could delete the Redis entry created below and cause a false-negative test result.
        await Task.Delay(3000);

        // Resolve CachedPermissionService via IPermissionService so the result is written to Redis.
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            var hasPermission = await svc.CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
            hasPermission.Should().BeTrue("member has the role that grants ViewChannel");
        }

        // Verify key is now cached
        var cacheKey = CachedPermissionService.CacheKey(memberId, channelId);
        var mux = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var redis = mux.GetDatabase();

        (await redis.KeyExistsAsync(cacheKey)).Should().BeTrue("cache must be populated after a CheckPermission call");

        // Revoke role (triggers Service Bus event)
        await RevokeRoleAsync(ownerToken, channelId, memberId, roleId);

        // Poll until the worker deletes the cache key (≤ 5 s)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (!await redis.KeyExistsAsync(cacheKey))
                break;

            await Task.Delay(100);
        }

        (await redis.KeyExistsAsync(cacheKey))
            .Should().BeFalse("CacheInvalidationWorker must have deleted the cache entry after role revocation");

        // Verify CachedPermissionService re-reads from DB
        using (var scope = _factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPermissionService>();
            var hasPermission = await svc.CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
            hasPermission.Should().BeFalse("role was revoked, so DB now returns no permissions");
        }
    }
}
