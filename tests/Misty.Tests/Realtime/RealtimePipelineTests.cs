using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Realtime;

[Collection("Integration")]
public sealed class RealtimePipelineTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public RealtimePipelineTests(ApiFactory factory)
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

    private HubConnection BuildConnection(string token)
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/realtime");
        return new HubConnectionBuilder()
            .WithUrl(url + $"?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
    }

    [Fact]
    public async Task SendMessage_FullPipeline_SignalRClientReceivesMessageCreated()
    {
        var (token, _) = await RegisterAndLoginAsync("pipeline_user1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "pipeline-ch1",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var conn = BuildConnection(token);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("MessageCreated", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        try
        {
            var idempotencyKey = Guid.NewGuid().ToString();
            var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
            {
                Content = "end-to-end pipeline message",
                IdempotencyKey = idempotencyKey,
            });
            msgResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var messageId = (await msgResp.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("messageId").GetGuid();

            await using var db = _factory.CreateDbContext();
            var outbox = await db.OutboxMessages
                .FirstOrDefaultAsync(o => o.MessageId == messageId);
            outbox.Should().NotBeNull("a pending OutboxMessage must be written in the same transaction as the Message");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => received.TrySetCanceled());

            var payload = await received.Task;

            payload.GetProperty("messageId").GetGuid().Should().Be(messageId);
            payload.GetProperty("channelId").GetGuid().Should().Be(channelId);
            payload.GetProperty("content").GetString().Should().Be("end-to-end pipeline message");
        }
        finally
        {
            await conn.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_NonMemberConnected_DoesNotReceiveChannelMessage()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("pipeline_owner2");
        var (outsiderToken, _) = await RegisterAndLoginAsync("pipeline_outsider2");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "pipeline-ch2",
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("channelId").GetGuid();

        var outsiderConn = BuildConnection(outsiderToken);
        var unexpectedlyReceived = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        outsiderConn.On<JsonElement>("MessageCreated", payload => unexpectedlyReceived.TrySetResult(payload));
        await outsiderConn.StartAsync();

        try
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            var msgResp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/messages", new
            {
                Content = "should not reach outsider",
                IdempotencyKey = Guid.NewGuid().ToString(),
            });
            msgResp.StatusCode.Should().Be(HttpStatusCode.Created);

            var completed = await Task.WhenAny(
                unexpectedlyReceived.Task,
                Task.Delay(TimeSpan.FromSeconds(1)));

            completed.Should().NotBe(unexpectedlyReceived.Task,
                "a non-member must not receive SignalR events for a channel they did not join");
        }
        finally
        {
            await outsiderConn.StopAsync();
        }
    }
}
