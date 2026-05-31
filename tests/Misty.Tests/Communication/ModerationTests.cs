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

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class ModerationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ModerationTests(ApiFactory factory)
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

    private async Task<(HttpResponseMessage Response, Guid ActionId)> ApplyModerationAsync(
        string issuerToken, Guid channelId, Guid targetUserId,
        ModerationActionType type, DateTime? expiresAt = null, string reason = "test reason")
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuerToken);
        var resp = await _client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/members/{targetUserId}/moderation",
            new { Type = (int)type, Reason = reason, ExpiresAt = expiresAt });

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return (resp, body.GetProperty("actionId").GetGuid());
        }
        return (resp, Guid.Empty);
    }

    private async Task<HttpResponseMessage> RevokeModerationAsync(
        string issuerToken, Guid channelId, Guid targetUserId, Guid actionId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuerToken);
        return await _client.DeleteAsync(
            $"/api/v1/channels/{channelId}/members/{targetUserId}/moderation/{actionId}");
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
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetGuid();
    }

    private async Task AssignRoleAsync(string ownerToken, Guid channelId, Guid memberId, Guid roleId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<bool> CheckPermissionAsync(Guid userId, Guid channelId, ChannelPermission permission)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PermissionService>();
        return await svc.CheckPermissionAsync(userId, channelId, permission);
    }

    [Fact]
    public async Task ApplyBan_PersistsAndDeniesPermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner1");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member1");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch1");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var canView = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        canView.Should().BeTrue("member with ViewChannel role must have access before ban");

        var (resp, actionId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Ban);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        actionId.Should().NotBe(Guid.Empty);

        var denied = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        denied.Should().BeFalse("banned user must be denied all permissions");
    }

    [Fact]
    public async Task ApplyDuplicateType_Returns409Conflict()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner2");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member2");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch2");
        await JoinChannelAsync(memberToken, channelId);

        var (first, _) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Mute);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var (second, _) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Mute);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a second active mute on the same user in the same channel must be rejected");
    }

    [Fact]
    public async Task RevokeBan_RestoresPermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner3");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member3");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch3");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        var (_, actionId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Ban);

        var denied = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        denied.Should().BeFalse();

        var revokeResp = await RevokeModerationAsync(ownerToken, channelId, memberId, actionId);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restored = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        restored.Should().BeTrue("revoking the ban must restore member access");
    }

    [Fact]
    public async Task RevokeAlreadyRevoked_Returns409Conflict()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner4");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member4");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch4");
        await JoinChannelAsync(memberToken, channelId);

        var (_, actionId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Warn);

        await RevokeModerationAsync(ownerToken, channelId, memberId, actionId);
        var second = await RevokeModerationAsync(ownerToken, channelId, memberId, actionId);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "revoking an already-revoked action must return 409");
    }

    [Fact]
    public async Task RevokeUnknownAction_Returns404NotFound()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner5");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch5");

        var resp = await RevokeModerationAsync(ownerToken, channelId, Guid.NewGuid(), Guid.NewGuid());
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApplyMute_RemovesWritePermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner6");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member6");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch6");
        await JoinChannelAsync(memberToken, channelId);

        // Give member send permission via a role
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var roleResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Member",
            Permissions = (long)(ChannelPermission.ViewChannel | ChannelPermission.SendMessages),
        });
        roleResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("roleId").GetGuid();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);

        var canSend = await CheckPermissionAsync(memberId, channelId, ChannelPermission.SendMessages);
        canSend.Should().BeTrue();

        await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Mute);

        var canSendAfter = await CheckPermissionAsync(memberId, channelId, ChannelPermission.SendMessages);
        canSendAfter.Should().BeFalse("a muted user must not send messages");

        var canViewAfter = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        canViewAfter.Should().BeTrue("a muted user can still view the channel");
    }

    [Fact]
    public async Task GetActiveActions_ReturnsCurrentlyActiveOnly()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner7");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member7");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch7");
        await JoinChannelAsync(memberToken, channelId);

        // Apply ban and warn; then revoke the warn
        var (_, banId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Ban);
        var (_, warnId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Warn);
        await RevokeModerationAsync(ownerToken, channelId, memberId, warnId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var getResp = await _client.GetAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/moderation");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var actions = await getResp.Content.ReadFromJsonAsync<JsonElement[]>();
        actions.Should().HaveCount(1, "only the active ban should be returned");
        actions![0].GetProperty("actionId").GetGuid().Should().Be(banId);
    }

    [Fact]
    public async Task ApplyWarn_DoesNotAffectPermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_owner8");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_member8");
        var channelId = await CreateChannelAsync(ownerToken, "mod-ch8");
        await JoinChannelAsync(memberToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var roleResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Reader",
            Permissions = (long)ChannelPermission.ViewChannel,
        });
        var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("roleId").GetGuid();
        await _client.PostAsync($"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);

        var (applyResp, _) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Warn);
        applyResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var canView = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        canView.Should().BeTrue("a warned user's permissions must not be affected");
    }

    [Fact]
    public async Task Apply_Self_Returns403()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("mod_self_owner");
        var channelId = await CreateChannelAsync(ownerToken, "mod-self-ch");

        var (resp, _) = await ApplyModerationAsync(ownerToken, channelId, ownerId, ModerationActionType.Mute);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Apply_TargetIsOwner_Returns403()
    {
        var (ownerToken, ownerId) = await RegisterAndLoginAsync("mod_owner_target");
        var (modToken, modId) = await RegisterAndLoginAsync("mod_actor1");
        var channelId = await CreateChannelAsync(ownerToken, "mod-owner-tgt");
        await JoinChannelAsync(modToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.BanMembers);
        await AssignRoleAsync(ownerToken, channelId, modId, roleId);

        var (resp, _) = await ApplyModerationAsync(modToken, channelId, ownerId, ModerationActionType.Ban);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Apply_ActorWithoutPermission_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_perm_owner");
        var (actorToken, _) = await RegisterAndLoginAsync("mod_perm_actor");
        var (targetToken, targetId) = await RegisterAndLoginAsync("mod_perm_target");
        var channelId = await CreateChannelAsync(ownerToken, "mod-perm-ch");
        await JoinChannelAsync(actorToken, channelId);
        await JoinChannelAsync(targetToken, channelId);

        var (resp, _) = await ApplyModerationAsync(actorToken, channelId, targetId, ModerationActionType.Mute);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an actor with no MuteMembers role must not be able to mute");
    }

    [Fact]
    public async Task Apply_ActorWithMuteOnly_CanMuteButCannotBan()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_mute_owner");
        var (actorToken, actorId) = await RegisterAndLoginAsync("mod_mute_actor");
        var (targetToken, targetId) = await RegisterAndLoginAsync("mod_mute_target");
        var channelId = await CreateChannelAsync(ownerToken, "mod-mute-ch");
        await JoinChannelAsync(actorToken, channelId);
        await JoinChannelAsync(targetToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.MuteMembers);
        await AssignRoleAsync(ownerToken, channelId, actorId, roleId);

        var (muteResp, _) = await ApplyModerationAsync(actorToken, channelId, targetId, ModerationActionType.Mute);
        muteResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var (banResp, _) = await ApplyModerationAsync(actorToken, channelId, targetId, ModerationActionType.Ban);
        banResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Apply_TypeKick_IsRejected()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_kick_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_kick_member");
        var channelId = await CreateChannelAsync(ownerToken, "mod-kick-ch");
        await JoinChannelAsync(memberToken, channelId);

        var (resp, _) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Kick);
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Revoke_ActorWithoutPermission_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_revoke_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_revoke_member");
        var channelId = await CreateChannelAsync(ownerToken, "mod-revoke-ch");
        await JoinChannelAsync(memberToken, channelId);

        var (_, actionId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Mute);

        // The muted member tries to revoke their own action -- they have no MuteMembers perm.
        var resp = await RevokeModerationAsync(memberToken, channelId, memberId, actionId);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoke_KickAction_Returns409()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_revoke_kick_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_revoke_kick_member");
        var channelId = await CreateChannelAsync(ownerToken, "mod-revoke-kick-ch");
        await JoinChannelAsync(memberToken, channelId);

        // Kick via the dedicated endpoint
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var kickReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/members/{memberId}")
        {
            Content = JsonContent.Create(new { Reason = "test kick" }),
        };
        var kickResp = await _client.SendAsync(kickReq);
        kickResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var kickBody = await kickResp.Content.ReadFromJsonAsync<JsonElement>();
        var actionId = kickBody.GetProperty("actionId").GetGuid();

        var revokeResp = await RevokeModerationAsync(ownerToken, channelId, memberId, actionId);
        revokeResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "kick actions are historical and must not be revocable");
    }

    [Fact]
    public async Task GetActiveActions_ExcludesKickActions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_kick_active_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_kick_active_member");
        var channelId = await CreateChannelAsync(ownerToken, "mod-kick-active-ch");
        await JoinChannelAsync(memberToken, channelId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var kickReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/members/{memberId}")
        {
            Content = JsonContent.Create(new { Reason = "test kick" }),
        };
        (await _client.SendAsync(kickReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await _client.GetAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/moderation");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var actions = await getResp.Content.ReadFromJsonAsync<JsonElement[]>();
        actions.Should().BeEmpty("kick actions are historical and must not appear in active actions");
    }

    [Fact]
    public async Task ExpiredBan_IsNotDuplicateForApply_AndIsNotEnforcedByPermissions()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mod_exp_owner");
        var (memberToken, memberId) = await RegisterAndLoginAsync("mod_exp_member");
        var channelId = await CreateChannelAsync(ownerToken, "mod-exp-ch");
        await JoinChannelAsync(memberToken, channelId);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Seed an already-expired ban directly (the validator would reject a past ExpiresAt over the wire).
        await using (var db = _factory.CreateDbContext())
        {
            var expired = ModerationAction.Create(
                Guid.NewGuid(),
                channelId,
                memberId,
                issuedByUserId: memberId, // value irrelevant for this test
                ModerationActionType.Ban,
                reason: "expired ban",
                expiresAt: DateTime.UtcNow.AddMinutes(-5));
            db.ModerationActions.Add(expired);
            await db.SaveChangesAsync();
        }

        // PermissionService must NOT enforce the expired ban.
        var allowed = await CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        allowed.Should().BeTrue("an expired ban is no longer active and must not deny permissions");

        // Apply handler must NOT treat the expired ban as a duplicate active sanction.
        var (resp, actionId) = await ApplyModerationAsync(ownerToken, channelId, memberId, ModerationActionType.Ban);
        resp.StatusCode.Should().Be(HttpStatusCode.Created,
            "an expired ban must not count as an active duplicate when applying a new ban");
        actionId.Should().NotBe(Guid.Empty);
    }
}
