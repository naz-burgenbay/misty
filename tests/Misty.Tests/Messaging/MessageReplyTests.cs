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
public sealed class MessageReplyTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public MessageReplyTests(ApiFactory factory)
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
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created);
        var regBody = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var userId = regBody.GetProperty("userId").GetGuid();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Username = username,
            Password = "Str0ngPass!",
        });
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private async Task<Guid> CreateChannelAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();
    }

    private async Task<Guid> SendAsync(Guid channelId, string content, Guid? parentMessageId = null)
    {
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = content,
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentMessageId,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();
    }

    [Fact]
    public async Task SendReply_ValidParent_Succeeds()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelId = await CreateChannelAsync("reply-ok-ch");
        var parentId = await SendAsync(channelId, "Parent message");
        var replyId = await SendAsync(channelId, "Reply message", parentId);

        await using var db = _factory.CreateDbContext();
        var reply = await db.Messages.FirstAsync(m => m.Id == replyId);
        reply.ParentMessageId.Should().Be(parentId);
    }

    [Fact]
    public async Task SendReply_ToReply_Returns422()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelId = await CreateChannelAsync("reply-nested-ch");
        var parentId = await SendAsync(channelId, "Top-level");
        var replyId = await SendAsync(channelId, "First reply", parentId);

        // Attempt to reply to the reply (must be rejected)
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Reply to reply",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = replyId,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the application layer must reject replies to replies");
    }

    [Fact]
    public async Task SendReply_ParentInDifferentChannel_Returns422()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user3");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var chA = await CreateChannelAsync("reply-cross-a");
        var chB = await CreateChannelAsync("reply-cross-b");
        var parentInA = await SendAsync(chA, "Parent in A");

        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{chB}/messages", new
        {
            Content = "Reply targeting parent in another channel",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentInA,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SendReply_NonexistentParent_Returns422()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user4");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelId = await CreateChannelAsync("reply-missing-ch");

        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Reply to ghost",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = Guid.NewGuid(),
        });

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetMessages_IncludesParentPreview()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user5");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelId = await CreateChannelAsync("reply-preview-ch");
        var parentId = await SendAsync(channelId, "Original parent content");
        var replyId = await SendAsync(channelId, "Reply", parentId);

        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        var reply = messages.First(m => m.GetProperty("id").GetGuid() == replyId);
        reply.GetProperty("parentMessageId").GetGuid().Should().Be(parentId);

        var preview = reply.GetProperty("parentPreview");
        preview.ValueKind.Should().Be(JsonValueKind.Object, "reply must include parent preview metadata");
        preview.GetProperty("id").GetGuid().Should().Be(parentId);
        preview.GetProperty("content").GetString().Should().Be("Original parent content");
        preview.GetProperty("isDeleted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetMessages_TombstonedParent_PreviewReflectsTombstone()
    {
        var (token, _) = await RegisterAndLoginAsync("reply_user6");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelId = await CreateChannelAsync("reply-tombstone-ch");
        var parentId = await SendAsync(channelId, "Parent to be tombstoned");
        var replyId = await SendAsync(channelId, "Reply that outlives parent", parentId);

        // Deleting the parent while a reply exists tombstones it.
        var del = await _client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{parentId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        var messages = (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messages").EnumerateArray().ToList();

        var reply = messages.First(m => m.GetProperty("id").GetGuid() == replyId);
        var preview = reply.GetProperty("parentPreview");

        preview.ValueKind.Should().Be(JsonValueKind.Object,
            "tombstoned parents must still be referenced in the reply preview by ID");
        preview.GetProperty("id").GetGuid().Should().Be(parentId);
        preview.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        preview.GetProperty("content").GetString().Should().BeEmpty(
            "tombstone preview must reflect the cleared content");
    }
}
