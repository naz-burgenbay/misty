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
public sealed class ChannelInviteTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public ChannelInviteTests(ApiFactory factory)
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
            SchemasToInclude = ["users", "comm", "msg"],
        });
        await _respawner.ResetAsync(conn);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid UserId, string Username)> RegisterAndLoginAsync(string username)
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
        return (loginBody.GetProperty("accessToken").GetString()!, userId, username);
    }

    private async Task<Guid> CreateChannelAsync(string ownerToken, bool isPrivate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ch-" + Guid.NewGuid().ToString("N")[..8],
            IsPrivate = isPrivate,
            IsAiAssistantEnabled = false,
            DefaultPermissions = (long)(ChannelPermission.ViewChannel | ChannelPermission.ReadHistory | ChannelPermission.SendMessages),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task JoinChannelAsync(string token, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendInviteAsync(string token, Guid channelId, string username)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/invites", new { Username = username });
    }

    [Fact]
    public async Task SendInvite_WithoutInvitePermission_Returns403_NoRow_NoOutbox()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_np_owner");
        var (memberToken, _, _) = await RegisterAndLoginAsync("ci_np_member");
        var (_, _, targetUsername) = await RegisterAndLoginAsync("ci_np_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: false);
        await JoinChannelAsync(memberToken, channelId);

        var resp = await SendInviteAsync(memberToken, channelId, targetUsername);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        (await db.ChannelInvites.CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync(o => o.EventType == "ChannelInviteSent")).Should().Be(0);
    }

    [Fact]
    public async Task SendInvite_AsOwner_Returns201_PersistsInvite_WritesOutbox()
    {
        var (ownerToken, ownerId, _) = await RegisterAndLoginAsync("ci_ok_owner");
        var (_, targetId, targetUsername) = await RegisterAndLoginAsync("ci_ok_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);

        var resp = await SendInviteAsync(ownerToken, channelId, targetUsername);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites.SingleAsync();
        invite.ChannelId.Should().Be(channelId);
        invite.InvitedByUserId.Should().Be(ownerId);
        invite.InvitedUserId.Should().Be(targetId);
        invite.Status.Should().Be(ChannelInviteStatus.Pending);

        var outbox = await db.OutboxMessages
            .Where(o => o.EventType == "ChannelInviteSent")
            .ToListAsync();
        outbox.Should().HaveCount(1);
        outbox[0].Topic.Should().Be("channel-invite-events");
    }

    [Fact]
    public async Task SendInvite_DuplicatePending_Returns409()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dup_owner");
        var (_, _, targetUsername) = await RegisterAndLoginAsync("ci_dup_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);

        (await SendInviteAsync(ownerToken, channelId, targetUsername)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var dup = await SendInviteAsync(ownerToken, channelId, targetUsername);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SendInvite_TargetAlreadyMember_Returns409()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_mem_owner");
        var (memberToken, _, memberUsername) = await RegisterAndLoginAsync("ci_mem_member");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: false);
        await JoinChannelAsync(memberToken, channelId);

        var resp = await SendInviteAsync(ownerToken, channelId, memberUsername);
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SendInvite_TargetHasBlockedInviter_Returns403()
    {
        var (ownerToken, ownerId, _) = await RegisterAndLoginAsync("ci_blk_owner");
        var (targetToken, _, targetUsername) = await RegisterAndLoginAsync("ci_blk_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);
        (await _client.PostAsJsonAsync($"/api/v1/users/{ownerId}/block", new { })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var resp = await SendInviteAsync(ownerToken, channelId, targetUsername);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        (await db.ChannelInvites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AcceptInvite_CreatesExactlyOneMembership_AndDelegatesToJoinHandler()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_acc_owner");
        var (targetToken, targetId, targetUsername) = await RegisterAndLoginAsync("ci_acc_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        (await SendInviteAsync(ownerToken, channelId, targetUsername)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites.SingleAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);
        var accept = await _client.PostAsync($"/api/v1/channels/invites/{invite.Id}/accept", content: null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = _factory.CreateDbContext();
        // Exactly one membership row for the invited user (the owner already has theirs from channel creation).
        var memberships = await db2.Memberships.Where(m => m.ChannelId == channelId && m.UserId == targetId).ToListAsync();
        memberships.Should().HaveCount(1, "join handler inserts exactly one Membership; the invite-accept handler must not insert its own row");

        var updatedInvite = await db2.ChannelInvites.SingleAsync(i => i.Id == invite.Id);
        updatedInvite.Status.Should().Be(ChannelInviteStatus.Accepted);
    }

    [Fact]
    public async Task DeclineInvite_MarksDeclined()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dec_owner");
        var (targetToken, _, targetUsername) = await RegisterAndLoginAsync("ci_dec_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        (await SendInviteAsync(ownerToken, channelId, targetUsername)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites.SingleAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);
        var decline = await _client.PostAsync($"/api/v1/channels/invites/{invite.Id}/decline", content: null);
        decline.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = _factory.CreateDbContext();
        var updated = await db2.ChannelInvites.SingleAsync(i => i.Id == invite.Id);
        updated.Status.Should().Be(ChannelInviteStatus.Declined);
    }
}
