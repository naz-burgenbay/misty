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
public sealed class MessagePaginationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public MessagePaginationTests(ApiFactory factory)
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

    // The critical test: two messages with the same CreatedAt timestamp must both appear in pagination results exactly once.
    // Cursor-based pagination using only CreatedAt would skip one of them; using (CreatedAt, Id) as the stable sort key prevents this.
    [Fact]
    public async Task GetMessages_EqualTimestamps_NoDuplicationOrOmission()
    {
        // Arrange: create channel and two messages with identical CreatedAt timestamps
        var (token, userId) = await RegisterAndLoginAsync("pagination_user");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "pagination-test-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        // Send two messages (their timestamps may differ by milliseconds depending on execution speed).
        // To force equal timestamps, we'll directly insert them in the DB with the same CreatedAt.
        var msg1Resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Message 1",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        msg1Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var msg1Id = (await msg1Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        var msg2Resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Message 2",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        msg2Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var msg2Id = (await msg2Resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        // Force identical CreatedAt timestamps in the DB
        await using var db = _factory.CreateDbContext();
        var sharedTimestamp = DateTime.UtcNow.AddMinutes(-5);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [msg].[Message] SET [CreatedAt] = {sharedTimestamp} WHERE [Id] IN ({msg1Id}, {msg2Id})");

        // Act: paginate with pageSize=1 to force cursor usage across the boundary
        var page1Resp = await _client.GetAsync($"/api/v1/channels/{channelId}/messages?pageSize=1");
        page1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1 = await page1Resp.Content.ReadFromJsonAsync<JsonElement>();
        var page1Messages = page1.GetProperty("messages").EnumerateArray().ToList();
        page1Messages.Should().HaveCount(1, "pageSize=1 must return exactly 1 message");

        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrEmpty("nextCursor must be present when more messages exist");

        var page2Resp = await _client.GetAsync($"/api/v1/channels/{channelId}/messages?pageSize=1&cursor={Uri.EscapeDataString(cursor!)}");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2 = await page2Resp.Content.ReadFromJsonAsync<JsonElement>();
        var page2Messages = page2.GetProperty("messages").EnumerateArray().ToList();
        page2Messages.Should().HaveCount(1, "second page must return the remaining message");

        // Assert: both messages appear exactly once across the two pages
        var allMessageIds = page1Messages.Concat(page2Messages)
            .Select(m => m.GetProperty("id").GetGuid())
            .ToList();

        allMessageIds.Should().HaveCount(2, "both messages must appear");
        allMessageIds.Should().Contain(msg1Id, "message 1 must be present");
        allMessageIds.Should().Contain(msg2Id, "message 2 must be present");
        allMessageIds.Should().OnlyHaveUniqueItems("no message should appear twice");
    }

    [Fact]
    public async Task EditMessage_ValidRequest_UpdatesContent()
    {
        // Arrange
        var (token, _) = await RegisterAndLoginAsync("edit_user");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "edit-test-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Original content",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        var messageId = (await msgResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        // Act
        var editResp = await _client.PutAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages/{messageId}",
            new { Content = "Edited content" });

        editResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert
        await using var db = _factory.CreateDbContext();
        var message = await db.Messages.FirstAsync(m => m.Id == messageId);
        message.Content.Should().Be("Edited content");
        message.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteMessage_NoReplies_HardDeletes()
    {
        // Arrange
        var (token, _) = await RegisterAndLoginAsync("delete_user1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "delete-test-ch1",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Will be hard-deleted",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        var messageId = (await msgResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        // Act
        var deleteResp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{messageId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: message is completely removed
        await using var db = _factory.CreateDbContext();
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        message.Should().BeNull("message with no replies should be hard-deleted");
    }

    [Fact]
    public async Task DeleteMessage_HasReplies_Tombstones()
    {
        // Arrange
        var (token, _) = await RegisterAndLoginAsync("delete_user2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "delete-test-ch2",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        // Parent message
        var parentResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Parent message",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        var parentId = (await parentResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        // Reply message
        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Reply",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentId,
        });

        // Act: delete the parent message
        var deleteResp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{parentId}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert: message is tombstoned (content cleared, IsDeleted=true, but row still exists)
        await using var db = _factory.CreateDbContext();
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == parentId);
        message.Should().NotBeNull("message with replies should be tombstoned, not hard-deleted");
        message!.IsDeleted.Should().BeTrue();
        message.Content.Should().BeEmpty("tombstoned message content should be cleared");
    }

    [Fact]
    public async Task GetMessages_IncludesTombstones()
    {
        // Arrange
        var (token, _) = await RegisterAndLoginAsync("tombstone_user");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "tombstone-test-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        // Parent + reply to force tombstone
        var parentResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Parent",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        var parentId = (await parentResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Reply",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentId,
        });

        await _client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{parentId}");

        // Act: retrieve messages
        var getResp = await _client.GetAsync($"/api/v1/channels/{channelId}/messages");
        var messages = (await getResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messages").EnumerateArray().ToList();

        // Assert: tombstoned parent appears in results
        var tombstone = messages.FirstOrDefault(m => m.GetProperty("id").GetGuid() == parentId);
        tombstone.ValueKind.Should().NotBe(JsonValueKind.Undefined, "tombstoned message must appear in pagination");
        tombstone.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        tombstone.GetProperty("content").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task EditMessage_Tombstone_Returns422()
    {
        // Arrange
        var (token, _) = await RegisterAndLoginAsync("edit_tombstone_user");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "edit-tombstone-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var parentResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Parent",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        var parentId = (await parentResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "Reply",
            IdempotencyKey = Guid.NewGuid().ToString(),
            ParentMessageId = parentId,
        });

        await _client.DeleteAsync($"/api/v1/channels/{channelId}/messages/{parentId}");

        // Act: attempt to edit the tombstoned message
        var editResp = await _client.PutAsJsonAsync(
            $"/api/v1/channels/{channelId}/messages/{parentId}",
            new { Content = "Should fail" });

        // Assert
        editResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "editing a tombstoned message must be rejected");
    }
}
