using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class ChannelAdminPermissionTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ChannelAdminPermissionTests(ApiFactory factory)
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

    private async Task<(Guid ChannelId, string Version)> CreateChannelAsync(string ownerToken, string name)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("channelId").GetGuid(), body.GetProperty("version").GetString()!);
    }

    private async Task JoinAsync(string memberToken, Guid channelId)
    {
        UseToken(memberToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateRoleAsync(string ownerToken, Guid channelId, string name = "TestRole", ChannelPermission perms = ChannelPermission.ViewChannel)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = name,
            Permissions = (long)perms,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.OutboxMessages.CountAsync();
    }

    [Fact]
    public async Task UpdateChannel_ActorWithoutManageChannel_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_upd_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_upd_actor");
        var (channelId, version) = await CreateChannelAsync(ownerToken, "cap-upd-ch");
        await JoinAsync(actorToken, channelId);

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}", new
        {
            Name = "renamed",
            IsAiAssistantEnabled = true,
            DefaultPermissions = 7L,
            Version = version,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var get = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}");
        get.GetProperty("name").GetString().Should().Be("cap-upd-ch");
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task DeleteChannel_ActorNotOwner_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_del_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_del_actor");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-del-ch");
        await JoinAsync(actorToken, channelId);

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var get = await _client.GetAsync($"/api/v1/channels/{channelId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK, "channel must not be soft-deleted");
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task CreateChannelRole_ActorWithoutManageRoles_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_crr_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_crr_actor");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-crr-ch");
        await JoinAsync(actorToken, channelId);

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Sneaky",
            Permissions = (long)ChannelPermission.ManageChannel,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var roles = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}/roles");
        roles.EnumerateArray().Any(r => r.GetProperty("name").GetString() == "Sneaky").Should().BeFalse();
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task UpdateChannelRole_ActorWithoutManageRoles_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_urr_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_urr_actor");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-urr-ch");
        await JoinAsync(actorToken, channelId);
        var roleId = await CreateRoleAsync(ownerToken, channelId, "Original");

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}/roles/{roleId}", new
        {
            Name = "Hijacked",
            Permissions = (long)ChannelPermission.ManageChannel,
            Version = "AAAAAAAAAAA=",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var roles = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}/roles");
        var role = roles.EnumerateArray().Single(r => r.GetProperty("roleId").GetGuid() == roleId);
        role.GetProperty("name").GetString().Should().Be("Original");
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task DeleteChannelRole_ActorWithoutManageRoles_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_drr_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_drr_actor");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-drr-ch");
        await JoinAsync(actorToken, channelId);
        var roleId = await CreateRoleAsync(ownerToken, channelId, "Keep");

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/roles/{roleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var roles = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}/roles");
        roles.EnumerateArray().Any(r => r.GetProperty("roleId").GetGuid() == roleId).Should().BeTrue();
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task AssignRole_ActorWithoutManageRoles_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_asn_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_asn_actor");
        var (targetToken, targetId) = await RegisterAndLoginAsync("cap_asn_target");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-asn-ch");
        await JoinAsync(actorToken, channelId);
        await JoinAsync(targetToken, channelId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.PostAsync($"/api/v1/channels/{channelId}/members/{targetId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var revoke = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{targetId}/roles/{roleId}");
        revoke.StatusCode.Should().Be(HttpStatusCode.NotFound, "role must not have been assigned");
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task AssignRole_OwnerRole_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_asnown_owner");
        var (targetToken, targetId) = await RegisterAndLoginAsync("cap_asnown_target");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-asnown-ch");
        await JoinAsync(targetToken, channelId);

        UseToken(ownerToken);
        var roles = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}/roles");
        var ownerRoleId = roles.EnumerateArray()
            .First(r => r.GetProperty("isOwnerRole").GetBoolean())
            .GetProperty("roleId").GetGuid();

        var resp = await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{targetId}/roles/{ownerRoleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeRole_ActorWithoutManageRoles_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("cap_rvk_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("cap_rvk_actor");
        var (targetToken, targetId) = await RegisterAndLoginAsync("cap_rvk_target");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-rvk-ch");
        await JoinAsync(actorToken, channelId);
        await JoinAsync(targetToken, channelId);
        var roleId = await CreateRoleAsync(ownerToken, channelId);

        UseToken(ownerToken);
        var assign = await _client.PostAsync($"/api/v1/channels/{channelId}/members/{targetId}/roles/{roleId}", null);
        assign.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var outboxBefore = await OutboxCountAsync();

        UseToken(actorToken);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{targetId}/roles/{roleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        UseToken(ownerToken);
        var reassign = await _client.PostAsync($"/api/v1/channels/{channelId}/members/{targetId}/roles/{roleId}", null);
        reassign.StatusCode.Should().Be(HttpStatusCode.Conflict, "role must still be assigned");
        (await OutboxCountAsync()).Should().Be(outboxBefore);
    }

    [Fact]
    public async Task RevokeRole_OwnerRoleFromCreator_Returns403()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("cap_rvkown_owner");
        var (channelId, _) = await CreateChannelAsync(ownerToken, "cap-rvkown-ch");

        UseToken(ownerToken);
        var roles = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/channels/{channelId}/roles");
        var ownerRoleId = roles.EnumerateArray()
            .First(r => r.GetProperty("isOwnerRole").GetBoolean())
            .GetProperty("roleId").GetGuid();

        var resp = await _client.DeleteAsync(
            $"/api/v1/channels/{channelId}/members/{ownerId}/roles/{ownerRoleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
