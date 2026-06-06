using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Messaging;

[Collection("Integration")]
public sealed class MessageReactionTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public MessageReactionTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
        var reg = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            Email = $"{username}@test.misty",
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> CreateChannelAsync(string name, long defaultPermissions = 0L)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = defaultPermissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();
    }

    private async Task JoinChannelAsync(Guid channelId)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created);
    }

    private async Task<Guid> SendAsync(Guid channelId, string content)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = content,
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();
    }

    private Task<HttpResponseMessage> AddReactionAsync(Guid channelId, Guid messageId, string emojiCode)
        => _client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages/{messageId}/reactions",
            new { EmojiCode = emojiCode });

    private Task<HttpResponseMessage> RemoveReactionAsync(Guid channelId, Guid messageId, string emojiCode)
        => _client.DeleteAsync(
            $"/api/v1/channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(emojiCode)}");

    [Fact]
    public async Task AddReaction_Succeeds_AndAppearsInHistory()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user1");
        SetToken(token);

        var channelId = await CreateChannelAsync("react-add-ch");
        var msgId = await SendAsync(channelId, "Hello");

        var add = await AddReactionAsync(channelId, msgId, "👍");
        add.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hist = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        hist.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await hist.Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("id").GetGuid() == msgId);
        var reactions = msg.GetProperty("reactions").EnumerateArray().ToList();
        reactions.Should().HaveCount(1);
        reactions[0].GetProperty("emojiCode").GetString().Should().Be("👍");
        reactions[0].GetProperty("count").GetInt32().Should().Be(1);
        reactions[0].GetProperty("reactedByMe").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AddReaction_Duplicate_IsIdempotent()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user2");
        SetToken(token);

        var channelId = await CreateChannelAsync("react-dup-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await AddReactionAsync(channelId, msgId, "🎉")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AddReactionAsync(channelId, msgId, "🎉")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var count = await db.MessageReactions.CountAsync(r => r.MessageId == msgId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task RemoveReaction_DropsCount()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user3");
        SetToken(token);

        var channelId = await CreateChannelAsync("react-remove-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await AddReactionAsync(channelId, msgId, "❤")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RemoveReactionAsync(channelId, msgId, "❤")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        (await db.MessageReactions.CountAsync(r => r.MessageId == msgId)).Should().Be(0);
    }

    [Fact]
    public async Task RemoveReaction_Nonexistent_IsIdempotent()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user4");
        SetToken(token);

        var channelId = await CreateChannelAsync("react-rm-missing-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await RemoveReactionAsync(channelId, msgId, "🤷")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AddReaction_OnTombstone_Returns422()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user5");
        SetToken(token);

        var channelId = await CreateChannelAsync("react-tombstone-ch");
        var parentId = await SendAsync(channelId, "Will be tombstoned");

        // Make it a tombstone by sending a reply and then deleting the parent.
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Child reply",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentId,
        });
        string delVer;
        await using (var db0 = _factory.CreateDbContext())
            delVer = Convert.ToBase64String((await db0.Messages.FirstAsync(m => m.Id == parentId)).Version);
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/channels/{channelId}/messages/{parentId}")
        {
            Content = JsonContent.Create(new { Version = delVer }),
        };
        var del = await _client.SendAsync(delReq);
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var add = await AddReactionAsync(channelId, parentId, "👀");
        add.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task TwoUsers_SameEmoji_CountIs2()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("react_userA");
        SetToken(tokenA);
        var channelId = await CreateChannelAsync("react-multi-ch");
        var msgId = await SendAsync(channelId, "Hi everyone");
        (await AddReactionAsync(channelId, msgId, "🔥")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Create a role that grants ViewChannel | ReadHistory | AddReactions for the joiner.
        // 1 (ViewChannel) | 2 (ReadHistory) | 16 (AddReactions) = 19
        var roleResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "Reactor",
            Permissions = 19L,
        });
        roleResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetGuid();

        var (tokenB, userBId) = await RegisterAndLoginAsync("react_userB");
        SetToken(tokenB);
        await JoinChannelAsync(channelId);

        SetToken(tokenA);
        (await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{userBId}/roles/{roleId}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        SetToken(tokenB);
        (await AddReactionAsync(channelId, msgId, "🔥")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hist = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        var body = await hist.Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("id").GetGuid() == msgId);
        var reactions = msg.GetProperty("reactions").EnumerateArray().ToList();
        reactions.Should().HaveCount(1);
        reactions[0].GetProperty("count").GetInt32().Should().Be(2);
        reactions[0].GetProperty("reactedByMe").GetBoolean().Should().BeTrue("user B is the current viewer");
    }

    [Fact]
    public async Task SameUser_DifferentEmojis_BothAppear()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user6");
        SetToken(token);
        var channelId = await CreateChannelAsync("react-multi-emoji-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await AddReactionAsync(channelId, msgId, "👍")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AddReactionAsync(channelId, msgId, "🎉")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var hist = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        var body = await hist.Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.GetProperty("messages").EnumerateArray().First(m => m.GetProperty("id").GetGuid() == msgId);
        var emojis = msg.GetProperty("reactions").EnumerateArray()
            .Select(r => r.GetProperty("emojiCode").GetString())
            .ToHashSet();
        emojis.Should().BeEquivalentTo(new[] { "👍", "🎉" });
    }

    [Fact]
    public async Task AddReaction_WritesOutboxRow_WithReactionAddedEventType()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user7");
        SetToken(token);
        var channelId = await CreateChannelAsync("react-outbox-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await AddReactionAsync(channelId, msgId, "🚀")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var outboxRow = await db.OutboxMessages
            .Where(o => o.MessageId == msgId && o.EventType == "ReactionAdded")
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        outboxRow.Should().NotBeNull();
        outboxRow!.Topic.Should().Be("message-events");
        outboxRow.Payload.Should().Contain("\"EventType\":\"ReactionAdded\"");
    }

    [Fact]
    public async Task RemoveReaction_WritesOutboxRow_WithReactionRemovedEventType()
    {
        var (token, _) = await RegisterAndLoginAsync("react_user8");
        SetToken(token);
        var channelId = await CreateChannelAsync("react-outbox-rm-ch");
        var msgId = await SendAsync(channelId, "Hi");

        (await AddReactionAsync(channelId, msgId, "✨")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RemoveReactionAsync(channelId, msgId, "✨")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var removedRow = await db.OutboxMessages
            .Where(o => o.MessageId == msgId && o.EventType == "ReactionRemoved")
            .FirstOrDefaultAsync();

        removedRow.Should().NotBeNull();
        removedRow!.Topic.Should().Be("message-events");
        removedRow.Payload.Should().Contain("\"EventType\":\"ReactionRemoved\"");
    }
}
