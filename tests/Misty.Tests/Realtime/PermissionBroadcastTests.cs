using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Realtime;

[Collection("Integration")]
public sealed class PermissionBroadcastTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public PermissionBroadcastTests(ApiFactory factory)
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

    private HubConnection BuildConnection(string token)
    {
        var url = new Uri(_factory.Server.BaseAddress, "hubs/realtime");
        return new HubConnectionBuilder()
            .WithUrl(url + $"?access_token={token}",
                opts => opts.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
    }

    private async Task<Guid> CreateChannelAsync(string ownerToken, string name)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = name,
            IsPrivate = false,
            IsAiAssistantEnabled = false,
            DefaultPermissions = 0L,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("channelId").GetGuid();
    }

    private async Task<Guid> CreateRoleAsync(string ownerToken, Guid channelId, ChannelPermission permissions)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "PermBcastRole",
            Permissions = (long)permissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    [Fact]
    public async Task AssignRole_FullPipeline_TargetReceivesRoleChangedWithinOneOutboxCycle()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("permbcast_owner1");
        var (memberToken, memberId) = await RegisterAndLoginAsync("permbcast_member1");
        var channelId = await CreateChannelAsync(ownerToken, "permbcast-ch1");

        SetToken(memberToken);
        (await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);

        var conn = BuildConnection(memberToken);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("RoleChanged", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        try
        {
            SetToken(ownerToken);
            var assignResp = await _client.PostAsync(
                $"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
            assignResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => received.TrySetCanceled());

            var payload = await received.Task;
            payload.GetProperty("channelId").GetGuid().Should().Be(channelId);
            payload.GetProperty("userId").GetGuid().Should().Be(memberId);
        }
        finally
        {
            await conn.StopAsync();
        }
    }

    [Fact]
    public async Task Kick_FullPipeline_TargetReceivesMembershipChangedWithinOneOutboxCycle()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("permbcast_owner2");
        var (memberToken, memberId) = await RegisterAndLoginAsync("permbcast_member2");
        var channelId = await CreateChannelAsync(ownerToken, "permbcast-ch2");

        SetToken(memberToken);
        (await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var conn = BuildConnection(memberToken);
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conn.On<JsonElement>("MembershipChanged", payload => received.TrySetResult(payload));
        await conn.StartAsync();

        try
        {
            SetToken(ownerToken);
            var kickResp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{memberId}");
            kickResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => received.TrySetCanceled());

            var payload = await received.Task;
            payload.GetProperty("channelId").GetGuid().Should().Be(channelId);
            payload.GetProperty("userId").GetGuid().Should().Be(memberId);
        }
        finally
        {
            await conn.StopAsync();
        }
    }
}
