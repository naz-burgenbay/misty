using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Communication.Contracts;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class BlockTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public BlockTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId)> RegisterAndLoginAsync(string username)
    {
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
        return (loginBody.GetProperty("accessToken").GetString()!, userId);
    }

    private async Task<HttpResponseMessage> BlockAsync(string token, Guid targetId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsJsonAsync($"/api/v1/users/{targetId}/block", new { });
    }

    private async Task<HttpResponseMessage> UnblockAsync(string token, Guid targetId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.DeleteAsync($"/api/v1/users/{targetId}/block");
    }

    private IUserBlockService GetBlockService()
        => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<IUserBlockService>();

    [Fact]
    public async Task Block_Returns204AndPersistsBlock()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("block_a");
        var (_, userB) = await RegisterAndLoginAsync("block_b");

        var resp = await BlockAsync(tokenA, userB);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var isBlocked = await GetBlockService().IsBlockedAsync(userA, userB);
        isBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task Block_IsBidirectional()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("block_bi_a");
        var (_, userB) = await RegisterAndLoginAsync("block_bi_b");

        await BlockAsync(tokenA, userB);

        var svc = GetBlockService();
        (await svc.IsBlockedAsync(userA, userB)).Should().BeTrue("A blocked B");
        (await svc.IsBlockedAsync(userB, userA)).Should().BeTrue("B is also blocked from A's perspective");
    }

    [Fact]
    public async Task Block_IsIdempotent()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("block_idem_a");
        var (_, userB) = await RegisterAndLoginAsync("block_idem_b");

        var r1 = await BlockAsync(tokenA, userB);
        var r2 = await BlockAsync(tokenA, userB);

        r1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        r2.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BlockSelf_Returns422()
    {
        var (token, userId) = await RegisterAndLoginAsync("block_self");

        var resp = await BlockAsync(token, userId);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Unblock_Returns204AndRemovesBlock()
    {
        var (tokenA, userA) = await RegisterAndLoginAsync("unblock_a");
        var (_, userB) = await RegisterAndLoginAsync("unblock_b");

        await BlockAsync(tokenA, userB);
        var resp = await UnblockAsync(tokenA, userB);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var isBlocked = await GetBlockService().IsBlockedAsync(userA, userB);
        isBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Unblock_WhenNoBlockExists_IsIdempotent()
    {
        var (tokenA, _) = await RegisterAndLoginAsync("unblock_none_a");
        var (_, userB) = await RegisterAndLoginAsync("unblock_none_b");

        var resp = await UnblockAsync(tokenA, userB);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task IsBlocked_ReturnsFalseWhenNoBlockExists()
    {
        var (_, userA) = await RegisterAndLoginAsync("noblock_a");
        var (_, userB) = await RegisterAndLoginAsync("noblock_b");

        var isBlocked = await GetBlockService().IsBlockedAsync(userA, userB);
        isBlocked.Should().BeFalse();
    }
}
