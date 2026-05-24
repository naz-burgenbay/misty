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
public sealed class PermissionTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public PermissionTests(ApiFactory factory)
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
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var loginResp = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
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

    private async Task<bool> CheckAsync(Guid userId, Guid channelId, ChannelPermission permission)
    {
    // Resolves the SQL-backed permission service directly to avoid cache state affecting these tests.
    // CachedPermissionService behaviour is covered separately.
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PermissionService>();
        return await svc.CheckPermissionAsync(userId, channelId, permission);
    }

    private async Task InsertModerationActionAsync(
        Guid channelId, Guid targetUserId, Guid issuedByUserId,
        ModerationActionType type, DateTime? expiresAt = null)
    {
        await using var db = _factory.CreateDbContext();
        db.ModerationActions.Add(ModerationAction.Create(
            Guid.NewGuid(), channelId, targetUserId, issuedByUserId, type, expiresAt));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RoleAssignment_GrantsExpectedPermission()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("perm_owner1");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member1");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch1");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.SendMessages);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var result = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RoleRevocation_RemovesPermission()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("perm_owner2");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member2");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch2");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.SendMessages);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var beforeRevoke = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        beforeRevoke.Should().BeTrue();

        await RevokeRoleAsync(ownerToken, channelId, memberId, roleId);

        var afterRevoke = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        afterRevoke.Should().BeFalse();
    }

    [Fact]
    public async Task UnrelatedUser_HasNoPermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("perm_owner3");
        var (_, outsiderId) = await RegisterAndLoginAsync("perm_outsider3");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch3");

        var result = await CheckAsync(outsiderId, channelId, ChannelPermission.ViewChannel);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task BannedUser_IsDeniedAllPermissions()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("perm_owner4");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member4");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch4");
        await JoinChannelAsync(memberToken, channelId);

        // Give the member full read permission via a role
        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel | ChannelPermission.ReadHistory);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var beforeBan = await CheckAsync(memberId, channelId, ChannelPermission.ViewChannel);
        beforeBan.Should().BeTrue();

        await InsertModerationActionAsync(channelId, memberId, ownerId, ModerationActionType.Ban);

        var afterBan = await CheckAsync(memberId, channelId, ChannelPermission.ViewChannel);
        afterBan.Should().BeFalse("a banned user must be denied all permissions");
    }

    [Fact]
    public async Task MutedUser_IsDeniedWritePermissions()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("perm_owner5");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member5");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch5");
        await JoinChannelAsync(memberToken, channelId);

        // Give member read + write
        var roleId = await CreateRoleAsync(ownerToken, channelId,
            ChannelPermission.ViewChannel | ChannelPermission.SendMessages | ChannelPermission.AttachFiles);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var canSendBefore = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        canSendBefore.Should().BeTrue();

        await InsertModerationActionAsync(channelId, memberId, ownerId, ModerationActionType.Mute);

        var canSendAfter = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        canSendAfter.Should().BeFalse("a muted user must not send messages");

        var canAttachAfter = await CheckAsync(memberId, channelId, ChannelPermission.AttachFiles);
        canAttachAfter.Should().BeFalse("a muted user must not attach files");

        // Read-class permissions are not affected by a mute
        var canViewAfter = await CheckAsync(memberId, channelId, ChannelPermission.ViewChannel);
        canViewAfter.Should().BeTrue("a muted user can still view the channel");
    }

    [Fact]
    public async Task ExpiredBan_NoLongerDeniesAccess()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("perm_owner6");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member6");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch6");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Insert an already-expired ban
        await InsertModerationActionAsync(channelId, memberId, ownerId, ModerationActionType.Ban,
            expiresAt: DateTime.UtcNow.AddSeconds(-1));

        var result = await CheckAsync(memberId, channelId, ChannelPermission.ViewChannel);
        result.Should().BeTrue("an expired ban must not block access");
    }

    [Fact]
    public async Task MultipleRoles_PermissionsAreAggregated()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("perm_owner7");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member7");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch7");
        await JoinChannelAsync(memberToken, channelId);

        // Role A grants ViewChannel; Role B grants SendMessages
        var roleAId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        var roleBId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.SendMessages);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleAId);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleBId);

        var canView = await CheckAsync(memberId, channelId, ChannelPermission.ViewChannel);
        canView.Should().BeTrue();

        var canSend = await CheckAsync(memberId, channelId, ChannelPermission.SendMessages);
        canSend.Should().BeTrue();

        // ManageChannel was granted by neither role
        var canManage = await CheckAsync(memberId, channelId, ChannelPermission.ManageChannel);
        canManage.Should().BeFalse();
    }

    [Fact]
    public async Task CacheIsPopulatedAfterFirstMiss()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("perm_owner8");
        var (memberToken, memberId) = await RegisterAndLoginAsync("perm_member8");
        var channelId = await CreateChannelAsync(ownerToken, "perm-ch8");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Wait for CacheInvalidationWorker to drain setup-time events (join + role-assign) before warming the cache, to avoid a race where the worker deletes the key we just wrote.
        await Task.Delay(3000);

        // Evict any pre-existing cache entry so we start from a clean miss.
        var mux = _factory.Services.GetRequiredService<IConnectionMultiplexer>();
        var redis = mux.GetDatabase();
        var key = CachedPermissionService.CacheKey(memberId, channelId);
        await redis.KeyDeleteAsync(key);

        // First check via the decorated service. Must hit SQL (cache miss), populate the cache and return true.
        using var scope = _factory.Services.CreateScope();
        var cachedSvc = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var result = await cachedSvc.CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        result.Should().BeTrue();

        // Assert the cache key now exists with a value that encodes ViewChannel.
        var cached = await redis.StringGetAsync(key);
        cached.HasValue.Should().BeTrue("the cache must be populated after the first miss");

        var cachedLong = (long)cached;
        cachedLong.Should().NotBe(long.MinValue, "a member with a role must not be stored as the denied sentinel");
        ((ChannelPermission)cachedLong & ChannelPermission.ViewChannel)
            .Should().Be(ChannelPermission.ViewChannel,
                "the cached value must include the ViewChannel flag");
    }
}
