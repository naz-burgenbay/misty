using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class MembershipTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public MembershipTests(ApiFactory factory)
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

    private async Task<(Guid ChannelId, string? InviteCode)> CreateChannelAsync(string token, bool isPrivate = false)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "test-channel",
            IsPrivate = isPrivate,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 7L,
        });
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = body.GetProperty("channelId").GetGuid();
        string? inviteCode = body.TryGetProperty("inviteCode", out var ic) && ic.ValueKind != JsonValueKind.Null
            ? ic.GetString()
            : null;
        return (channelId, inviteCode);
    }

    [Fact]
    public async Task JoinPublicChannel_Returns200WithMembershipData()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner1");
        var (joinerToken, joinerId) = await RegisterAndLoginAsync("mem_joiner1");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("membershipId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("channelId").GetGuid().Should().Be(channelId);
    }

    [Fact]
    public async Task JoinPrivateChannel_WithCorrectCode_Returns200()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner2");
        var (joinerToken, _) = await RegisterAndLoginAsync("mem_joiner2");
        var (channelId, inviteCode) = await CreateChannelAsync(ownerToken, isPrivate: true);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { InviteCode = inviteCode });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task JoinPrivateChannel_WithWrongCode_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner3");
        var (joinerToken, _) = await RegisterAndLoginAsync("mem_joiner3");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: true);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { InviteCode = "wrong-code" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task JoinChannel_AlreadyMember_Returns409()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner4");
        var (joinerToken, _) = await RegisterAndLoginAsync("mem_joiner4");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LeaveChannel_AsMember_Returns204()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner5");
        var (joinerToken, _) = await RegisterAndLoginAsync("mem_joiner5");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/leave");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LeaveChannel_NotMember_Returns404()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner6");
        var (nonMemberToken, _) = await RegisterAndLoginAsync("mem_nonmember6");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonMemberToken);
        var resp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/leave");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChannel_AfterCreation_MemberCountIsOne()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner7");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("memberCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task JoinAndLeave_MemberCountUpdatesCorrectly()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("mem_owner8");
        var (joinerToken, _) = await RegisterAndLoginAsync("mem_joiner8");
        var (channelId, _) = await CreateChannelAsync(ownerToken, isPrivate: false);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var afterJoin = await _client.GetAsync($"/api/v1/channels/{channelId}");
        var joinBody = await afterJoin.Content.ReadFromJsonAsync<JsonElement>();
        joinBody.GetProperty("memberCount").GetInt32().Should().Be(2);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", joinerToken);
        await _client.DeleteAsync($"/api/v1/channels/{channelId}/leave");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var afterLeave = await _client.GetAsync($"/api/v1/channels/{channelId}");
        var leaveBody = await afterLeave.Content.ReadFromJsonAsync<JsonElement>();
        leaveBody.GetProperty("memberCount").GetInt32().Should().Be(1);
    }
}
