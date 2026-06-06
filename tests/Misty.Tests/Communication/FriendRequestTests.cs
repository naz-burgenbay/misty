using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class FriendRequestTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public FriendRequestTests(ApiFactory factory)
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

    private async Task<HttpResponseMessage> SendRequestAsync(string token, string targetUsername)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsJsonAsync("/api/v1/friends/requests", new { Username = targetUsername });
    }

    private Task<HttpResponseMessage> SendJsonDeleteAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) };
        return _client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> AcceptRequestAsync(string token, Guid requestId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsync($"/api/v1/friends/requests/{requestId}/accept", content: null);
    }

    private async Task<HttpResponseMessage> DeclineRequestAsync(string token, Guid requestId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string version;
        await using (var db = _factory.CreateDbContext())
        {
            var entity = await db.FriendRequests.FirstOrDefaultAsync(f => f.Id == requestId);
            version = entity is null ? "AAAAAAAAAAA=" : Convert.ToBase64String(entity.Version);
        }
        return await _client.PostAsJsonAsync($"/api/v1/friends/requests/{requestId}/decline", new { Version = version });
    }

    private async Task<HttpResponseMessage> BlockAsync(string token, Guid targetId)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.PostAsJsonAsync($"/api/v1/users/{targetId}/block", new { });
    }

    [Fact]
    public async Task Send_Returns201_PersistsPendingRow_AndWritesOutbox()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fr_send_a");
        var (_, userB, usernameB) = await RegisterAndLoginAsync("fr_send_b");

        var resp = await SendRequestAsync(tokenA, usernameB);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var row = await db.FriendRequests.SingleAsync(r => r.SenderId == userA && r.ReceiverId == userB);
        row.Status.Should().Be(FriendRequestStatus.Pending);

        var outbox = await db.OutboxMessages
            .Where(o => o.EventType == "FriendRequestSent")
            .ToListAsync();
        outbox.Should().HaveCount(1);
        outbox[0].Topic.Should().Be("friend-events");
        outbox[0].Payload.Should().Contain(userA.ToString());
        outbox[0].Payload.Should().Contain(userB.ToString());
    }

    [Fact]
    public async Task Accept_CreatesFriendshipWithCanonicalOrdering_AndWritesOutbox()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fr_acc_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("fr_acc_b");

        await SendRequestAsync(tokenA, usernameB);

        await using var db = _factory.CreateDbContext();
        var request = await db.FriendRequests.SingleAsync(r => r.SenderId == userA && r.ReceiverId == userB);

        var accept = await AcceptRequestAsync(tokenB, request.Id);
        accept.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = _factory.CreateDbContext();
        var (expectedA, expectedB) = new System.Data.SqlTypes.SqlGuid(userA).CompareTo(new System.Data.SqlTypes.SqlGuid(userB)) < 0 ? (userA, userB) : (userB, userA);
        var friendship = await db2.Friendships.SingleAsync();
        friendship.UserAId.Should().Be(expectedA);
        friendship.UserBId.Should().Be(expectedB);

        var updated = await db2.FriendRequests.SingleAsync(r => r.Id == request.Id);
        updated.Status.Should().Be(FriendRequestStatus.Accepted);

        var outbox = await db2.OutboxMessages
            .Where(o => o.EventType == "FriendRequestAccepted")
            .ToListAsync();
        outbox.Should().HaveCount(1);
        outbox[0].Topic.Should().Be("friend-events");
    }

    [Fact]
    public async Task Decline_MarksDeclined_EmitsFriendRequestDeclined()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("fr_dec_a");
        var (tokenB, _, usernameB) = await RegisterAndLoginAsync("fr_dec_b");

        await SendRequestAsync(tokenA, usernameB);

        await using var db = _factory.CreateDbContext();
        var request = await db.FriendRequests.SingleAsync();

        var decline = await DeclineRequestAsync(tokenB, request.Id);
        decline.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = _factory.CreateDbContext();
        var updated = await db2.FriendRequests.SingleAsync(r => r.Id == request.Id);
        updated.Status.Should().Be(FriendRequestStatus.Declined);

        var declinedOutbox = await db2.OutboxMessages
            .Where(o => o.EventType == "FriendRequestDeclined")
            .ToListAsync();
        declinedOutbox.Should().HaveCount(1);
        declinedOutbox[0].Topic.Should().Be("friend-events");

        var acceptedOutbox = await db2.OutboxMessages.CountAsync(o => o.EventType == "FriendRequestAccepted");
        acceptedOutbox.Should().Be(0);
    }

    // Planned self-target returns 400, but the handler throws ValidationException which the global handler maps to 422 (same as the established BlockSelf_Returns422 convention).
    [Fact]
    public async Task SendToSelf_Returns422()
    {
        var (tokenA, _, usernameA) = await RegisterAndLoginAsync("fr_self_a");

        var resp = await SendRequestAsync(tokenA, usernameA);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await using var db = _factory.CreateDbContext();
        (await db.FriendRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SendDuplicate_EitherDirection_Returns409()
    {
        var (tokenA, _, usernameA) = await RegisterAndLoginAsync("fr_dup_a");
        var (tokenB, _, usernameB) = await RegisterAndLoginAsync("fr_dup_b");

        var first = await SendRequestAsync(tokenA, usernameB);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var dupSameDirection = await SendRequestAsync(tokenA, usernameB);
        dupSameDirection.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var dupReverseDirection = await SendRequestAsync(tokenB, usernameA);
        dupReverseDirection.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SendToUserWhoBlockedYou_Returns403_NoRow()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fr_blkby_a");
        var (tokenB, _, usernameB) = await RegisterAndLoginAsync("fr_blkby_b");

        (await BlockAsync(tokenB, userA)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp = await SendRequestAsync(tokenA, usernameB);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        (await db.FriendRequests.CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync(o => o.EventType == "FriendRequestSent")).Should().Be(0);
    }

    [Fact]
    public async Task SendToUserYouBlocked_Returns403_NoRow()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("fr_blked_a");
        var (_, userB, usernameB) = await RegisterAndLoginAsync("fr_blked_b");

        (await BlockAsync(tokenA, userB)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var resp = await SendRequestAsync(tokenA, usernameB);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        (await db.FriendRequests.CountAsync()).Should().Be(0);
        (await db.OutboxMessages.CountAsync(o => o.EventType == "FriendRequestSent")).Should().Be(0);
    }

    [Fact]
    public async Task GetReceived_ReturnsOnlyPending_NewestFirst()
    {
        var (tokenR, _, usernameR) = await RegisterAndLoginAsync("fr_rcv_r");
        var (tokenS1, _, _) = await RegisterAndLoginAsync("fr_rcv_s1");
        var (tokenS2, _, _) = await RegisterAndLoginAsync("fr_rcv_s2");
        var (tokenS3, _, _) = await RegisterAndLoginAsync("fr_rcv_s3");

        // S1 -> R, then S2 -> R, then S3 -> R. R declines S2's, leaving Pending {S1, S3}.
        (await SendRequestAsync(tokenS1, usernameR)).StatusCode.Should().Be(HttpStatusCode.Created);
        await Task.Delay(20);
        (await SendRequestAsync(tokenS2, usernameR)).StatusCode.Should().Be(HttpStatusCode.Created);
        await Task.Delay(20);
        (await SendRequestAsync(tokenS3, usernameR)).StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var s2Request = await db.FriendRequests.Where(r => r.Status == FriendRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt).Skip(1).Take(1).SingleAsync();
        (await DeclineRequestAsync(tokenR, s2Request.Id)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenR);
        var resp = await _client.GetAsync("/api/v1/friends/requests/received");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var list = items.EnumerateArray().ToList();
        list.Should().HaveCount(2);
        list.Select(i => i.GetProperty("status").GetString()).Should().AllBe("Pending");
        var timestamps = list.Select(i => i.GetProperty("createdAt").GetDateTime()).ToList();
        timestamps.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetSent_ReturnsOnlyPendingRequestsFromCaller()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fr_sent_a");
        var (_, userB, usernameB) = await RegisterAndLoginAsync("fr_sent_b");
        var (tokenC, _, _) = await RegisterAndLoginAsync("fr_sent_c");
        var (_, userD, usernameD) = await RegisterAndLoginAsync("fr_sent_d");

        // A sends to B; C sends to D. A's sent list should only contain B.
        (await SendRequestAsync(tokenA, usernameB)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await SendRequestAsync(tokenC, usernameD)).StatusCode.Should().Be(HttpStatusCode.Created);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var resp = await _client.GetAsync("/api/v1/friends/requests/sent");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await resp.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        items.Should().HaveCount(1);
        items[0].GetProperty("receiverId").GetGuid().Should().Be(userB);
        items[0].GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact]
    public async Task Cancel_BySender_Returns204_RequestNoLongerPending()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("fr_cancel_a");
        var (_, userB, usernameB) = await RegisterAndLoginAsync("fr_cancel_b");

        (await SendRequestAsync(tokenA, usernameB)).StatusCode.Should().Be(HttpStatusCode.Created);

        Guid requestId;
        await using (var db = _factory.CreateDbContext())
        {
            requestId = (await db.FriendRequests.SingleAsync(r => r.SenderId == userA && r.ReceiverId == userB)).Id;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        string canVer;
        await using (var db0 = _factory.CreateDbContext())
            canVer = Convert.ToBase64String((await db0.FriendRequests.FirstAsync(f => f.Id == requestId)).Version);
        var resp = await SendJsonDeleteAsync($"/api/v1/friends/requests/{requestId}", new { Version = canVer });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = _factory.CreateDbContext();
        var row = await db2.FriendRequests.SingleAsync(r => r.Id == requestId);
        row.Status.Should().NotBe(FriendRequestStatus.Pending);
    }

    [Fact]
    public async Task Cancel_ByNonSender_Returns403()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("fr_cancel2_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("fr_cancel2_b");

        (await SendRequestAsync(tokenA, usernameB)).StatusCode.Should().Be(HttpStatusCode.Created);

        Guid requestId;
        await using (var db = _factory.CreateDbContext())
        {
            requestId = (await db.FriendRequests.SingleAsync(r => r.ReceiverId == userB)).Id;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var resp = await SendJsonDeleteAsync($"/api/v1/friends/requests/{requestId}", new { Version = "AAAAAAAAAAA=" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
