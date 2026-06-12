using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Infrastructure.Messaging;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Messaging;

[Collection("Integration")]
public sealed class AIResponseWorkerTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public AIResponseWorkerTests(ApiFactory factory)
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

    [Fact]
    public async Task AIResponseWorker_EnabledChannel_WritesAiResponseMessage()
    {
        var (token, _) = await RegisterAndLoginAsync("ai_user1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ai-enabled-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = true,
            DefaultPermissions = 0L,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "@misty-bot Hello AI",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        msgResp.StatusCode.Should().Be(HttpStatusCode.Created,
            "the HTTP write path must succeed independently of the async AI worker");
        var originalMessageId = (await msgResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("messageId").GetGuid();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        Guid? aiMessageId = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            await using var db = _factory.CreateDbContext();
            var aiMsg = await db.Messages
                .AsNoTracking()
                .Where(m => m.AuthorId == AIResponseWorker.AiUserId && m.ChannelId == channelId)
                .FirstOrDefaultAsync();
            if (aiMsg is not null)
            {
                aiMessageId = aiMsg.Id;
                break;
            }
        }

        aiMessageId.Should().NotBeNull("AIResponseWorker must write a reply when IsAiAssistantEnabled=true");

        await using var dbFinal = _factory.CreateDbContext();
        var aiOutbox = await dbFinal.OutboxMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.MessageId == aiMessageId!.Value);
        aiOutbox.Should().NotBeNull("the AI reply must produce an OutboxMessage via the same write path");

        var aiMsgRecord = await dbFinal.Messages
            .AsNoTracking()
            .FirstAsync(m => m.Id == aiMessageId!.Value);
        aiMsgRecord.IdempotencyKey.Should().Be($"ai-response:{originalMessageId}");
    }

    [Fact]
    public async Task AIResponseWorker_DisabledChannel_SkipsResponse()
    {
        var (token, _) = await RegisterAndLoginAsync("ai_user2");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ai-disabled-ch",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
        {
            Content = "No AI here",
            IdempotencyKey = Guid.NewGuid().ToString(),
        });
        msgResp.StatusCode.Should().Be(HttpStatusCode.Created);

        await Task.Delay(TimeSpan.FromSeconds(5));

        await using var db = _factory.CreateDbContext();
        var aiMessages = await db.Messages
            .AsNoTracking()
            .Where(m => m.AuthorId == AIResponseWorker.AiUserId && m.ChannelId == channelId)
            .ToListAsync();

        aiMessages.Should().BeEmpty("AIResponseWorker must not write a reply when IsAiAssistantEnabled=false");
    }
}
