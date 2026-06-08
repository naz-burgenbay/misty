using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Infrastructure.Messaging;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public OutboxRelayTests(ApiFactory factory)
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
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("channelId").GetGuid();
    }

    private async Task<Guid> SendMessageAsync(string token, Guid channelId, string content)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = content,
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("messageId").GetGuid();
    }

    [Fact]
    public async Task SendMessage_CreatesOutboxRow_WithNullPublishedAt()
    {
        var (token, _) = await RegisterAndLoginAsync("outbox_owner1");
        var channelId = await CreateChannelAsync(token, "outbox-ch1");

        var messageId = await SendMessageAsync(token, channelId, "Hello outbox");

        await using var db = _factory.CreateDbContext();
        var outbox = await db.OutboxMessages.SingleOrDefaultAsync(o => o.MessageId == messageId);

        outbox.Should().NotBeNull("a write must produce exactly one OutboxMessage row");
        outbox!.PublishedAt.Should().BeNull("the relay has not yet run");
        outbox.Topic.Should().Be("message-events");
        outbox.Payload.Should().Contain(messageId.ToString());
    }

    [Fact]
    public async Task RelayWorker_MarksOutboxRowPublished()
    {
        var (token, _) = await RegisterAndLoginAsync("outbox_owner2");
        var channelId = await CreateChannelAsync(token, "outbox-ch2");

        var messageId = await SendMessageAsync(token, channelId, "Relay me");

        DateTime? publishedAt = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            await using var db = _factory.CreateDbContext();
            var outbox = await db.OutboxMessages.SingleOrDefaultAsync(o => o.MessageId == messageId);
            publishedAt = outbox?.PublishedAt;
            if (publishedAt is not null) break;
            await Task.Delay(500);
        }

        publishedAt.Should().NotBeNull("the outbox relay must mark the row published within 15 s");
    }

    [Fact]
    public async Task RelayWorker_DoesNotRepublish_AfterRowMarkedPublished()
    {
        var (token, _) = await RegisterAndLoginAsync("outbox_owner3");
        var channelId = await CreateChannelAsync(token, "outbox-ch3");

        var messageId = await SendMessageAsync(token, channelId, "Publish once");

        DateTime? firstPublishedAt = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!cts.IsCancellationRequested)
        {
            await using var db = _factory.CreateDbContext();
            var row = await db.OutboxMessages.SingleOrDefaultAsync(o => o.MessageId == messageId);
            firstPublishedAt = row?.PublishedAt;
            if (firstPublishedAt is not null) break;
            await Task.Delay(500);
        }

        firstPublishedAt.Should().NotBeNull();

        await Task.Delay(2000);

        await using var dbCheck = _factory.CreateDbContext();
        var outbox = await dbCheck.OutboxMessages.SingleAsync(o => o.MessageId == messageId);
        outbox.PublishedAt.Should().Be(firstPublishedAt,
            "a relay cycle after initial publish must not overwrite the timestamp");
    }

    [Fact]
    public async Task OutboxConcurrencyToken_SecondUpdate_ThrowsDbUpdateConcurrencyException()
    {
        var (token, _) = await RegisterAndLoginAsync("outbox_owner4");
        var channelId = await CreateChannelAsync(token, "outbox-ch4");
        var messageId = await SendMessageAsync(token, channelId, "Concurrent relay");

        await using var dbA = _factory.CreateDbContext();
        await using var dbB = _factory.CreateDbContext();

        var outboxA = await dbA.OutboxMessages.SingleAsync(o => o.MessageId == messageId);
        var outboxB = await dbB.OutboxMessages.SingleAsync(o => o.MessageId == messageId);

        outboxA.MarkPublished();
        await dbA.SaveChangesAsync();

        outboxB.MarkPublished();
        var act = async () => await dbB.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the rowversion token must prevent a second relay instance from overwriting the row");
    }

    [Fact]
    public async Task SendMessage_MessageAndOutbox_AreInSameTransaction()
    {
        var (token, _) = await RegisterAndLoginAsync("outbox_owner5");
        var channelId = await CreateChannelAsync(token, "outbox-ch5");

        var messageId = await SendMessageAsync(token, channelId, "Atomicity check");

        await using var db = _factory.CreateDbContext();
        var message = await db.Messages.FindAsync(messageId);
        var outbox  = await db.OutboxMessages.SingleOrDefaultAsync(o => o.MessageId == messageId);

        message.Should().NotBeNull();
        outbox.Should().NotBeNull();
        outbox!.MessageId.Should().Be(message!.Id);
    }
}
