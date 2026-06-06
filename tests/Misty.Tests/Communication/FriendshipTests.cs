using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class FriendshipTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public FriendshipTests(ApiFactory factory)
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

    private async Task<(string Token, Guid UserId, string Username)> RegisterAndLoginAsync(string username)
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
        return (loginBody.GetProperty("accessToken").GetString()!, userId, username);
    }

    private async Task BefriendAsync(string tokenA, string tokenB, string usernameB)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var send = await _client.PostAsJsonAsync("/api/v1/friends/requests", new { Username = usernameB });
        send.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var req = await db.FriendRequests.OrderByDescending(r => r.CreatedAt).FirstAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var accept = await _client.PostAsync($"/api/v1/friends/requests/{req.Id}/accept", content: null);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<List<Guid>> GetFriendIdsAsync(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.GetAsync("/api/v1/friends");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(e => e.GetProperty("userId").GetGuid()).ToList();
    }

    [Fact]
    public async Task RemoveFriend_HardDeletesRow_BothSidesNoLongerSeeEachOther()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fs_rm_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("fs_rm_b");

        await BefriendAsync(tokenA, tokenB, usernameB);

        (await GetFriendIdsAsync(tokenA)).Should().ContainSingle().Which.Should().Be(userB);
        (await GetFriendIdsAsync(tokenB)).Should().ContainSingle().Which.Should().Be(userA);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var del = await _client.DeleteAsync($"/api/v1/friends/{userB}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        (await db.Friendships.CountAsync()).Should().Be(0);

        (await GetFriendIdsAsync(tokenA)).Should().BeEmpty();
        (await GetFriendIdsAsync(tokenB)).Should().BeEmpty();
    }

    [Fact]
    public async Task Block_CascadesFriendship_EmitsBothBlockAndFriendshipRemovedEvents()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("fs_blk_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("fs_blk_b");

        await BefriendAsync(tokenA, tokenB, usernameB);

        await using (var dbBefore = _factory.CreateDbContext())
        {
            (await dbBefore.Friendships.CountAsync()).Should().Be(1);
        }

        // Snapshot outbox count before the block so we can isolate what the block produced.
        int outboxBefore;
        await using (var dbSnap = _factory.CreateDbContext())
        {
            outboxBefore = await dbSnap.OutboxMessages.CountAsync();
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var block = await _client.PostAsJsonAsync($"/api/v1/users/{userB}/block", new { });
        block.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        (await db.Friendships.CountAsync()).Should().Be(0, "block cascades the friendship hard-delete");
        (await db.UserBlocks.CountAsync()).Should().Be(1);

        var newOutbox = await db.OutboxMessages
            .OrderBy(o => o.CreatedAt)
            .Skip(outboxBefore)
            .ToListAsync();
        newOutbox.Should().Contain(o => o.EventType == "UserBlocked",
            "block must publish UserBlocked");
        newOutbox.Should().Contain(o => o.EventType == "FriendshipRemoved",
            "the cascaded friendship deletion must publish FriendshipRemoved for lifecycle completeness");
    }

    [Fact]
    public async Task GetFriends_ExcludesUsersBlockedInEitherDirection()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fs_gx_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("fs_gx_b");
        var (tokenC, userC, usernameC) = await RegisterAndLoginAsync("fs_gx_c");

        await BefriendAsync(tokenA, tokenB, usernameB);
        await BefriendAsync(tokenA, tokenC, usernameC);

        (await GetFriendIdsAsync(tokenA)).Should().BeEquivalentTo(new[] { userB, userC });

        // A blocks B -> friendship hard-deleted; A still sees C.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        (await _client.PostAsJsonAsync($"/api/v1/users/{userB}/block", new { })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await GetFriendIdsAsync(tokenA)).Should().BeEquivalentTo(new[] { userC });

        // C blocks A: A should still not see C in friends list (block is bidirectional).
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenC);
        (await _client.PostAsJsonAsync($"/api/v1/users/{userA}/block", new { })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await GetFriendIdsAsync(tokenA)).Should().BeEmpty();
    }
}
