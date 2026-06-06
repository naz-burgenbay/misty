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
public sealed class LifecycleEventPublishTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;
    private static int _seq;

    public LifecycleEventPublishTests(ApiFactory factory)
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

    private Task<HttpResponseMessage> SendJsonDeleteAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) };
        return _client.SendAsync(req);
    }

    private async Task<string> CurrentUserVersionAsync(Guid userId)
    {
        await using var db = _factory.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        return Convert.ToBase64String(user.Version);
    }

    // Friend request lifecycle 

    [Fact]
    public async Task FriendRequestDeclined_PublishesEvent()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_frd_a");
        var (tokenB, _, usernameB) = await RegisterAndLoginAsync("lfc_frd_b");

        var requestId = await SendFriendRequestAsync(tokenA, usernameB);
        SetToken(tokenB);
        string frVer;
        await using (var db0 = _factory.CreateDbContext())
            frVer = Convert.ToBase64String((await db0.FriendRequests.FirstAsync(f => f.Id == requestId)).Version);
        (await _client.PostAsJsonAsync($"/api/v1/friends/requests/{requestId}/decline", new { Version = frVer })).EnsureSuccessStatusCode();

        await AssertOutboxAsync("friend-events", "FriendRequestDeclined", requestId);
    }

    [Fact]
    public async Task FriendRequestCancelled_PublishesEvent()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_frc_a");
        var (_, _, usernameB) = await RegisterAndLoginAsync("lfc_frc_b");

        var requestId = await SendFriendRequestAsync(tokenA, usernameB);
        SetToken(tokenA);
        string frcVer;
        await using (var db0 = _factory.CreateDbContext())
            frcVer = Convert.ToBase64String((await db0.FriendRequests.FirstAsync(f => f.Id == requestId)).Version);
        (await SendJsonDeleteAsync($"/api/v1/friends/requests/{requestId}", new { Version = frcVer })).EnsureSuccessStatusCode();

        await AssertOutboxAsync("friend-events", "FriendRequestCancelled", requestId);
    }

    [Fact]
    public async Task FriendshipCreated_PublishesEventAlongsideFriendRequestAccepted()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_fsc_a");
        var (tokenB, _, usernameB) = await RegisterAndLoginAsync("lfc_fsc_b");

        var requestId = await SendFriendRequestAsync(tokenA, usernameB);
        SetToken(tokenB);
        (await _client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null)).EnsureSuccessStatusCode();

        await using var db = _factory.CreateDbContext();
        (await db.OutboxMessages.CountAsync(o => o.EventType == "FriendRequestAccepted")).Should().Be(1);
        var created = await db.OutboxMessages.SingleAsync(o => o.EventType == "FriendshipCreated");
        created.Topic.Should().Be("friend-events");
    }

    [Fact]
    public async Task FriendshipRemoved_PublishesEvent_OnExplicitRemove()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_frm_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("lfc_frm_b");

        await BefriendAsync(tokenA, tokenB, usernameB);

        SetToken(tokenA);
        (await _client.DeleteAsync($"/api/v1/friends/{userB}")).EnsureSuccessStatusCode();

        await using var db = _factory.CreateDbContext();
        var removed = await db.OutboxMessages.SingleAsync(o => o.EventType == "FriendshipRemoved");
        removed.Topic.Should().Be("friend-events");
    }

    // Channel invite lifecycle 

    [Fact]
    public async Task ChannelInviteDeclined_PublishesEvent()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("lfc_cid_o");
        var (targetToken, _, targetUsername) = await RegisterAndLoginAsync("lfc_cid_t");

        var channelId = await CreateChannelAsync(ownerToken);
        SetToken(ownerToken);
        var inviteResp = await _client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/invites", new { Username = targetUsername });
        inviteResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var inviteId = (await inviteResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        SetToken(targetToken);
        string ciVer;
        await using (var db0 = _factory.CreateDbContext())
            ciVer = Convert.ToBase64String((await db0.ChannelInvites.FirstAsync(i => i.Id == inviteId)).Version);
        (await _client.PostAsJsonAsync($"/api/v1/channels/invites/{inviteId}/decline", new { Version = ciVer })).EnsureSuccessStatusCode();

        await AssertOutboxAsync("channel-invite-events", "ChannelInviteDeclined", inviteId);
    }

    // Channel lifecycle 

    [Fact]
    public async Task ChannelCreated_PublishesEvent()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("lfc_chc_o");
        var channelId = await CreateChannelAsync(ownerToken);
        await AssertOutboxAsync("channel-events", "ChannelCreated", channelId);
    }

    [Fact]
    public async Task ChannelUpdated_PublishesEvent()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("lfc_chu_o");
        var channelId = await CreateChannelAsync(ownerToken);

        SetToken(ownerToken);
        var getResp = await _client.GetAsync($"/api/v1/channels/{channelId}");
        var version = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();

        var putResp = await _client.PutAsJsonAsync($"/api/v1/channels/{channelId}", new
        {
            Name = "renamed-" + Guid.NewGuid().ToString("N")[..6],
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
            Version = version,
        });
        putResp.EnsureSuccessStatusCode();

        await AssertOutboxAsync("channel-events", "ChannelUpdated", channelId);
    }

    [Fact]
    public async Task ChannelDeleted_PublishesEvent()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("lfc_chd_o");
        var channelId = await CreateChannelAsync(ownerToken);

        SetToken(ownerToken);
        (await _client.DeleteAsync($"/api/v1/channels/{channelId}")).EnsureSuccessStatusCode();

        await AssertOutboxAsync("channel-events", "ChannelDeleted", channelId);
    }

    // Block lifecycle 

    [Fact]
    public async Task UserBlocked_PublishesEvent()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_blk_a");
        var (_, userB, _) = await RegisterAndLoginAsync("lfc_blk_b");

        SetToken(tokenA);
        (await _client.PostAsJsonAsync($"/api/v1/users/{userB}/block", new { })).EnsureSuccessStatusCode();

        await AssertOutboxAsync("block-events", "UserBlocked", userB);
    }

    [Fact]
    public async Task UserUnblocked_PublishesEvent()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("lfc_unb_a");
        var (_, userB, _) = await RegisterAndLoginAsync("lfc_unb_b");

        SetToken(tokenA);
        (await _client.PostAsJsonAsync($"/api/v1/users/{userB}/block", new { })).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/api/v1/users/{userB}/block")).EnsureSuccessStatusCode();

        await AssertOutboxAsync("block-events", "UserUnblocked", userB);
    }

    // Channel role create 

    [Fact]
    public async Task ChannelRoleCreated_PublishesEvent()
    {
        var (ownerToken, _, _) = await RegisterAndLoginAsync("lfc_rol_o");
        var channelId = await CreateChannelAsync(ownerToken);

        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "LfcRole",
            Permissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await AssertOutboxAsync("role-events", "ChannelRoleCreated", channelId);
    }

    // User profile lifecycle 

    [Fact]
    public async Task UserRegistered_PublishesEvent()
    {
        var (_, userId, _) = await RegisterAndLoginAsync("lfc_reg_a");
        await AssertOutboxAsync("user-events", "UserRegistered", userId);
    }

    [Fact]
    public async Task UserProfileUpdated_PublishesEvent()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("lfc_upd_a");
        SetToken(token);

        var getResp = await _client.GetAsync($"/api/v1/users/{userId}");
        var version = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetString();

        var putResp = await _client.PutAsJsonAsync("/api/v1/users/me", new
        {
            DisplayName = "Lifecycle Updated",
            Bio = "bio",
            Version = version,
        });
        putResp.EnsureSuccessStatusCode();

        await AssertOutboxAsync("user-events", "UserProfileUpdated", userId);
    }

    [Fact]
    public async Task UserAvatarChanged_PublishesEvent_OnUpload()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("lfc_avu_a");
        SetToken(token);

        using var form = MakePngForm();
        form.Add(new StringContent(await CurrentUserVersionAsync(userId)), "version");
        (await _client.PostAsync("/api/v1/users/me/avatar", form)).EnsureSuccessStatusCode();

        await AssertOutboxAsync("user-events", "UserAvatarChanged", userId);
    }

    [Fact]
    public async Task UserAvatarChanged_PublishesEvent_OnRemove()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("lfc_avr_a");
        SetToken(token);

        using var form = MakePngForm();
        form.Add(new StringContent(await CurrentUserVersionAsync(userId)), "version");
        (await _client.PostAsync("/api/v1/users/me/avatar", form)).EnsureSuccessStatusCode();
        (await SendJsonDeleteAsync("/api/v1/users/me/avatar", new { Version = await CurrentUserVersionAsync(userId) })).EnsureSuccessStatusCode();

        // Two events should now exist for this user (upload + remove). Both share the same EventType.
        await using var db = _factory.CreateDbContext();
        var rows = await db.OutboxMessages
            .Where(o => o.EventType == "UserAvatarChanged" && o.MessageId == userId)
            .ToListAsync();
        rows.Should().HaveCount(2, "upload and remove must each publish UserAvatarChanged");
        rows.Should().AllSatisfy(r => r.Topic.Should().Be("user-events"));
    }

    [Fact]
    public async Task UserDeleted_PublishesEvent()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("lfc_del_a");
        SetToken(token);

        (await _client.DeleteAsync("/api/v1/users/me")).EnsureSuccessStatusCode();

        await AssertOutboxAsync("user-events", "UserDeleted", userId);
    }

    // Helpers 

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task AssertOutboxAsync(string topic, string eventType, Guid expectedIdInPayload)
    {
        await using var db = _factory.CreateDbContext();
        var row = await db.OutboxMessages
            .Where(o => o.EventType == eventType && o.Topic == topic)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        row.Should().NotBeNull(
            $"a '{eventType}' outbox row on topic '{topic}' must exist immediately after the HTTP action");
        row!.Payload.Should().Contain(expectedIdInPayload.ToString(),
            $"the typed payload for '{eventType}' must encode '{expectedIdInPayload}'");
    }

    private async Task<(string Token, Guid UserId, string Username)> RegisterAndLoginAsync(string prefix)
    {
        // Append a per-test sequence number so re-registering the same prefix in different tests stays unique within the run.
        var n = Interlocked.Increment(ref _seq);
        var username = $"{prefix}_{n}";
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

    private async Task<Guid> SendFriendRequestAsync(string senderToken, string targetUsername)
    {
        SetToken(senderToken);
        var resp = await _client.PostAsJsonAsync(
            "/api/v1/friends/requests", new { Username = targetUsername });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task BefriendAsync(string tokenA, string tokenB, string usernameB)
    {
        var requestId = await SendFriendRequestAsync(tokenA, usernameB);
        SetToken(tokenB);
        (await _client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", null))
            .EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateChannelAsync(string ownerToken)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "lfc-" + Guid.NewGuid().ToString("N")[..8],
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private static MultipartFormDataContent MakePngForm()
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(OnePxPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", "avatar.png");
        return form;
    }

    private static readonly byte[] OnePxPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, 0xDE, 0x00, 0x00, 0x00,
        0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
        0x00, 0x05, 0xFE, 0x02, 0xFE, 0xA8, 0xE3, 0x35, 0xB2, 0x00, 0x00, 0x00,
        0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
