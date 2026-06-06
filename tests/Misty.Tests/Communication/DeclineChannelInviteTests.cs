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
public sealed class DeclineChannelInviteTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public DeclineChannelInviteTests(ApiFactory factory)
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

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> CreateChannelAsync(string ownerToken, bool isPrivate)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ci-" + Guid.NewGuid().ToString("N")[..8],
            IsPrivate = isPrivate,
            IsAiAssistantEnabled = false,
            DefaultPermissions = (long)(ChannelPermission.ViewChannel | ChannelPermission.ReadHistory | ChannelPermission.SendMessages),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task<Guid> SendInviteAsync(string ownerToken, Guid channelId, string username)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/invites", new { Username = username });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites
            .Where(i => i.ChannelId == channelId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstAsync();
        return invite.Id;
    }

    [Fact]
    public async Task Decline_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.PostAsync($"/api/v1/channels/invites/{Guid.NewGuid()}/decline", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Decline_NonExistentInvite_Returns404()
    {
        var (token, _, _) = await RegisterAndLoginAsync("ci_dec_404");
        SetToken(token);

        var resp = await _client.PostAsync($"/api/v1/channels/invites/{Guid.NewGuid()}/decline", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Decline_ByInvitee_Returns204_MarksDeclined_NoMembership()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dec2_owner");
        var (targetToken, targetId, targetUsername) = await RegisterAndLoginAsync("ci_dec2_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        var inviteId = await SendInviteAsync(ownerToken, channelId, targetUsername);

        SetToken(targetToken);
        var resp = await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/decline", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites.SingleAsync(i => i.Id == inviteId);
        invite.Status.Should().Be(ChannelInviteStatus.Declined);

        var membershipExists = await db.Memberships
            .AnyAsync(m => m.ChannelId == channelId && m.UserId == targetId);
        membershipExists.Should().BeFalse("declining an invite must not create a membership row");
    }

    [Fact]
    public async Task Decline_ByNonInvitee_Returns403_AndInviteRemainsPending()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dec3_owner");
        var (_, _, targetUsername) = await RegisterAndLoginAsync("ci_dec3_target");
        var (attackerToken, _, _) = await RegisterAndLoginAsync("ci_dec3_attacker");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        var inviteId = await SendInviteAsync(ownerToken, channelId, targetUsername);

        SetToken(attackerToken);
        var resp = await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/decline", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        var invite = await db.ChannelInvites.SingleAsync(i => i.Id == inviteId);
        invite.Status.Should().Be(ChannelInviteStatus.Pending, "a forbidden decline must not mutate the invite");
    }

    [Fact]
    public async Task Decline_AlreadyAcceptedInvite_Returns409()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dec4_owner");
        var (targetToken, _, targetUsername) = await RegisterAndLoginAsync("ci_dec4_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        var inviteId = await SendInviteAsync(ownerToken, channelId, targetUsername);

        SetToken(targetToken);
        (await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/accept", content: null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var decline = await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/decline", content: null);
        decline.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Decline_AlreadyDeclinedInvite_Returns409()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("ci_dec5_owner");
        var (targetToken, _, targetUsername) = await RegisterAndLoginAsync("ci_dec5_target");

        var channelId = await CreateChannelAsync(ownerToken, isPrivate: true);
        var inviteId = await SendInviteAsync(ownerToken, channelId, targetUsername);

        SetToken(targetToken);
        (await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/decline", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsync($"/api/v1/channels/invites/{inviteId}/decline", content: null)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
    }
}
