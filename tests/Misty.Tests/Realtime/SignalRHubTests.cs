using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Misty.Api.Realtime;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Realtime;

[Collection("Integration")]
public sealed class SignalRHubTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public SignalRHubTests(ApiFactory factory)
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

    private HubConnection BuildConnection(string? token = null)
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/realtime");
        var builder = new HubConnectionBuilder()
            .WithUrl(url.ToString() + (token is not null ? $"?access_token={token}" : string.Empty),
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler());
        return builder.Build();
    }

    [Fact]
    public async Task ConnectWithValidToken_Succeeds()
    {
        var (token, _) = await RegisterAndLoginAsync("hubuser1");
        var conn = BuildConnection(token);

        await conn.StartAsync();

        conn.State.Should().Be(HubConnectionState.Connected);
        await conn.StopAsync();
    }

    [Fact]
    public async Task ConnectWithoutToken_IsRejected()
    {
        var conn = BuildConnection(token: null);

        var act = async () => await conn.StartAsync();

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }

    [Fact]
    public async Task ConnectWithInvalidToken_IsRejected()
    {
        var conn = BuildConnection("not.a.valid.jwt");

        var act = async () => await conn.StartAsync();

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*401*");
    }

    [Fact]
    public async Task OnConnect_JoinsChannelGroup_ReceivesGroupMessages()
    {
        var (token, userId) = await RegisterAndLoginAsync("hubuser4");
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var createResp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "hub-test-channel",
            IsPrivate = false,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var channelId = channelBody.GetProperty("channelId").GetGuid();

        var conn = BuildConnection(token);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("MessageCreated", payload => received.TrySetResult(payload));
        await conn.StartAsync();
        
        await Task.Delay(100);

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        var groupName = $"channel:{channelId}";
        await hubContext.Clients.Group(groupName).SendAsync("MessageCreated", "hello-group");

        var completedTask = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completedTask.Should().BeSameAs(received.Task, "client should receive the group message within 5 s");
        var payload = await received.Task;
        payload.Should().Be("hello-group");

        await conn.StopAsync();
    }

    [Fact]
    public async Task OnConnect_UserNotInChannel_DoesNotReceiveChannelGroupMessages()
    {
        var (token, _) = await RegisterAndLoginAsync("hubuser5");

        var conn = BuildConnection(token);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("MessageCreated", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        await hubContext.Clients.Group("channel:00000000-0000-0000-0000-000000000001").SendAsync("MessageCreated", "should-not-arrive");

        var completedTask = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completedTask.Should().NotBeSameAs(received.Task, "user is not a member of that channel");

        await conn.StopAsync();
    }

    [Fact]
    public async Task OnConnect_JoinsUserGroup_ReceivesUserTargetedMessages()
    {
        var (token, userId) = await RegisterAndLoginAsync("hubuser_usergroup");

        var conn = BuildConnection(token);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("RoleChanged", payload => received.TrySetResult(payload));
        await conn.StartAsync();
        await Task.Delay(100);

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        await hubContext.Clients.Group($"user:{userId}").SendAsync("RoleChanged", "user-targeted");

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(received.Task, "client should join user:{userId} group on connect");
        (await received.Task).Should().Be("user-targeted");

        await conn.StopAsync();
    }

    [Fact]
    public async Task OnConnect_DoesNotJoinOtherUsersUserGroup()
    {
        var (token, _) = await RegisterAndLoginAsync("hubuser_usergroup_iso");

        var conn = BuildConnection(token);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("RoleChanged", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        await hubContext.Clients.Group($"user:{Guid.NewGuid()}").SendAsync("RoleChanged", "should-not-arrive");

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().NotBeSameAs(received.Task, "user:{uid} group must be scoped to the connected user only");

        await conn.StopAsync();
    }

    [Fact]
    public async Task OnConnect_JoinsConversationGroup_ReceivesGroupMessages()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("hubuser_conv_a");
        var (tokenB, userBId) = await RegisterAndLoginAsync("hubuser_conv_b");

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);
        var convResp = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = userBId });
        convResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var convBody = await convResp.Content.ReadFromJsonAsync<JsonElement>();
        var conversationId = convBody.GetProperty("conversationId").GetGuid();

        var conn = BuildConnection(tokenB);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("MessageCreated", payload => received.TrySetResult(payload));
        await conn.StartAsync();
        await Task.Delay(100);

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        await hubContext.Clients.Group($"conversation:{conversationId}")
            .SendAsync("MessageCreated", "conv-group-hello");

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(received.Task, "participant should join conversation:{id} group on connect");
        (await received.Task).Should().Be("conv-group-hello");

        await conn.StopAsync();
    }

    [Fact]
    public async Task OnConnect_NonParticipant_DoesNotReceiveConversationGroupMessages()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("hubuser_conv_iso_a");
        var (_, userBId) = await RegisterAndLoginAsync("hubuser_conv_iso_b");
        var (tokenC, _) = await RegisterAndLoginAsync("hubuser_conv_iso_c");

        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);
        var convResp = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = userBId });
        convResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversationId = (await convResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("conversationId").GetGuid();

        var conn = BuildConnection(tokenC);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<string>("MessageCreated", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        var hubContext = _factory.Services.GetRequiredService<IHubContext<MistyHub>>();
        await hubContext.Clients.Group($"conversation:{conversationId}")
            .SendAsync("MessageCreated", "should-not-arrive");

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().NotBeSameAs(received.Task, "non-participant must not be in the conversation group");

        await conn.StopAsync();
    }
}
