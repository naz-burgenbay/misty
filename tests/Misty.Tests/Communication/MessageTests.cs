using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Domain.Communication;
using Misty.Infrastructure.Communication;
using Misty.Tests.Integration;
using Respawn;
using StackExchange.Redis;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class MessageTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public MessageTests(ApiFactory factory)
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
        var regResp = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            Username = username,
            DisplayName = $"{username} Display",
            Password = "Str0ngPass!",
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private async Task<Guid> CreateChannelAsync(string ownerToken, string name, long defaultPermissions = 0L)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = defaultPermissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("channelId").GetGuid();
    }

    private async Task JoinChannelAsync(string memberToken, Guid channelId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
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

    private async Task<HttpResponseMessage> SendMessageAsync(
        string token, Guid channelId, string content, string idempotencyKey, Guid? parentMessageId = null)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = content,
            IdempotencyKey = idempotencyKey,
            ParentMessageId = parentMessageId,
        });
    }

    [Fact]
    public async Task SendMessage_Persists_ReturnsMessageData()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("msg_owner1");
        var channelId = await CreateChannelAsync(ownerToken, "msg-ch1");

        // Owner has all permissions, send directly as owner
        var idempotencyKey = Guid.NewGuid().ToString();
        var resp = await SendMessageAsync(ownerToken, channelId, "Hello world", idempotencyKey);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        var messageId = body.GetProperty("messageId").GetGuid();
        messageId.Should().NotBeEmpty();
        body.GetProperty("content").GetString().Should().Be("Hello world");
        body.GetProperty("wasIdempotent").GetBoolean().Should().BeFalse();

        // Verify message persisted to DB
        await using var db = _factory.CreateDbContext();
        var stored = await db.Messages.FindAsync(messageId);
        stored.Should().NotBeNull();
        stored!.Content.Should().Be("Hello world");
    }

    [Fact]
    public async Task SendMessage_DuplicateIdempotencyKey_ReturnsOriginalMessage()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("msg_owner2");
        var channelId = await CreateChannelAsync(ownerToken, "msg-ch2");

        var idempotencyKey = Guid.NewGuid().ToString();

        // First send
        var first = await SendMessageAsync(ownerToken, channelId, "First send", idempotencyKey);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var originalId = firstBody.GetProperty("messageId").GetGuid();

        // Second send with same idempotency key (different content to prove we return original)
        var second = await SendMessageAsync(ownerToken, channelId, "Second attempt", idempotencyKey);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        secondBody.GetProperty("messageId").GetGuid().Should().Be(originalId,
            "duplicate idempotency key must return the original message");
        secondBody.GetProperty("wasIdempotent").GetBoolean().Should().BeTrue();
        secondBody.GetProperty("content").GetString().Should().Be("First send",
            "the original message content must be returned, not the duplicate attempt's content");
    }

    [Fact]
    public async Task SendMessage_WithoutSendPermission_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("msg_owner3");
        var (memberToken, _) = await RegisterAndLoginAsync("msg_member3");
        // Channel with no default permissions
        var channelId = await CreateChannelAsync(ownerToken, "msg-ch3", defaultPermissions: 0L);
        await JoinChannelAsync(memberToken, channelId);

        // Member has no roles and no default SendMessages permission
        var resp = await SendMessageAsync(memberToken, channelId, "Should be denied", Guid.NewGuid().ToString());

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a member without SendMessages permission must be rejected");
    }

    [Fact]
    public async Task SendMessage_BannedUser_Returns403()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("msg_owner4");
        var (memberToken, memberId) = await RegisterAndLoginAsync("msg_member4");
        // Channel where members can send by default
        var channelId = await CreateChannelAsync(ownerToken, "msg-ch4", defaultPermissions: 0L);
        await JoinChannelAsync(memberToken, channelId);

        // Grant member SendMessages via a role
        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.SendMessages);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Confirm member can send before the ban
        var beforeBan = await SendMessageAsync(memberToken, channelId, "Before ban", Guid.NewGuid().ToString());
        beforeBan.StatusCode.Should().Be(HttpStatusCode.Created, "member with SendMessages role must be able to send");

        // Ban the member
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var banResp = await _client.PostAsJsonAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/moderation",
            new { Type = 1 /* Ban */, ExpiresAt = (DateTime?)null });
        banResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Redis is cleared directly here so the test stays deterministic and does not depend on asynchronous Service Bus processing timing in the emulator. 
        // In production this invalidation happens through CacheInvalidationWorker.
        var redisDb = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redisDb.KeyDeleteAsync(CachedPermissionService.CacheKey(memberId, channelId));

        // With the cache cleared the permission service falls back to SQL,
        // which must reflect the ban and deny the request.
        var afterBan = await SendMessageAsync(memberToken, channelId, "After ban", Guid.NewGuid().ToString());
        afterBan.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a banned user must be denied SendMessages permission");
    }
}
