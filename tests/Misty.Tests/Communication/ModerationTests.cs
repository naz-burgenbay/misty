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
}
