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
public sealed class KickMemberTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public KickMemberTests(ApiFactory factory)
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
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
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

    private void UseToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> CreateChannelAsync(string ownerToken)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "kick-channel",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task JoinAsync(string token, Guid channelId)
    {
        UseToken(token);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateRoleWithPermissionAsync(string ownerToken, Guid channelId, ChannelPermission perms, string name)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = name,
            Permissions = (long)perms,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    private async Task AssignRoleAsync(string ownerToken, Guid channelId, Guid userId, Guid roleId)
    {
        UseToken(ownerToken);
        var resp = await _client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/members/{userId}/roles/{roleId}", new { });
        resp.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> KickAsync(string actorToken, Guid channelId, Guid targetUserId)
    {
        UseToken(actorToken);
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/members/{targetUserId}")
        {
            Content = JsonContent.Create(new { Reason = "test kick" }),
        };
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task Kick_AsOwner_Returns200_AndSoftDeletesMembership()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("kick_owner1");
        var (joinerToken, joinerId) = await RegisterAndLoginAsync("kick_target1");
        var channelId = await CreateChannelAsync(ownerToken);
        await JoinAsync(joinerToken, channelId);

        var resp = await KickAsync(ownerToken, channelId, joinerId);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("actionId").GetGuid().Should().NotBeEmpty();

        await using var db = _factory.CreateDbContext();
        var membership = await db.Memberships
            .IgnoreQueryFilters()
            .SingleAsync(m => m.ChannelId == channelId && m.UserId == joinerId);
        membership.IsDeleted.Should().BeTrue();
        membership.DeletedAt.Should().NotBeNull();

        var action = await db.ModerationActions
            .SingleAsync(a => a.ChannelId == channelId && a.TargetUserId == joinerId);
        action.Type.Should().Be(ModerationActionType.Kick);
        action.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Kick_TargetIsOwner_Returns403()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("kick_owner2");
        var (actorToken, actorId) = await RegisterAndLoginAsync("kick_actor2");
        var channelId = await CreateChannelAsync(ownerToken);
        await JoinAsync(actorToken, channelId);

        var kickRoleId = await CreateRoleWithPermissionAsync(
            ownerToken, channelId,
            ChannelPermission.ViewChannel | ChannelPermission.KickMembers,
            "Kicker");
        await AssignRoleAsync(ownerToken, channelId, actorId, kickRoleId);

        var resp = await KickAsync(actorToken, channelId, ownerId);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kick_Self_Returns403()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("kick_owner3");
        var channelId = await CreateChannelAsync(ownerToken);

        var resp = await KickAsync(ownerToken, channelId, ownerId);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kick_ActorWithoutPermission_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("kick_owner4");
        var (actorToken, _) = await RegisterAndLoginAsync("kick_actor4");
        var (targetToken, targetId) = await RegisterAndLoginAsync("kick_target4");
        var channelId = await CreateChannelAsync(ownerToken);
        await JoinAsync(actorToken, channelId);
        await JoinAsync(targetToken, channelId);

        var resp = await KickAsync(actorToken, channelId, targetId);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Kick_ThenTargetRejoins_CreatesNewMembershipRow()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("kick_owner5");
        var (targetToken, targetId) = await RegisterAndLoginAsync("kick_target5");
        var channelId = await CreateChannelAsync(ownerToken);
        await JoinAsync(targetToken, channelId);

        Guid firstMembershipId;
        await using (var db = _factory.CreateDbContext())
        {
            firstMembershipId = (await db.Memberships
                .SingleAsync(m => m.ChannelId == channelId && m.UserId == targetId)).Id;
        }

        var kickResp = await KickAsync(ownerToken, channelId, targetId);
        kickResp.StatusCode.Should().Be(HttpStatusCode.OK);

        UseToken(targetToken);
        var rejoinResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        rejoinResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejoinBody = await rejoinResp.Content.ReadFromJsonAsync<JsonElement>();
        var newMembershipId = rejoinBody.GetProperty("membershipId").GetGuid();
        newMembershipId.Should().NotBe(firstMembershipId);

        await using var db2 = _factory.CreateDbContext();
        var all = await db2.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.ChannelId == channelId && m.UserId == targetId)
            .ToListAsync();
        all.Should().HaveCount(2);
        all.Count(m => m.IsDeleted).Should().Be(1);
        all.Count(m => !m.IsDeleted).Should().Be(1);
        all.Single(m => !m.IsDeleted).Id.Should().Be(newMembershipId);
    }
}
