using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Communication;
using Misty.Domain.Communication;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class InboxTests : IAsyncLifetime
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public InboxTests(ApiFactory factory)
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

    private async Task<InboxItem> WaitForInboxItemAsync(Guid userId, InboxItemType type)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = _factory.CreateDbContext();
            var item = await db.InboxItems.FirstOrDefaultAsync(i => i.UserId == userId && i.Type == type);
            if (item is not null) return item;
            await Task.Delay(PollInterval);
        }
        throw new TimeoutException($"No InboxItem of type {type} for user {userId} arrived within {PollTimeout}.");
    }

    private async Task BefriendAsync(string tokenA, string tokenB, string usernameB)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        (await _client.PostAsJsonAsync("/api/v1/friends/requests", new { Username = usernameB })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        await using var db = _factory.CreateDbContext();
        var req = await db.FriendRequests.OrderByDescending(r => r.CreatedAt).FirstAsync();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        (await _client.PostAsync($"/api/v1/friends/requests/{req.Id}/accept", content: null)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FriendRequestSent_TriggersInboxItem_ForReceiver()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("ib_frs_a");
        var (_, userB, usernameB) = await RegisterAndLoginAsync("ib_frs_b");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        (await _client.PostAsJsonAsync("/api/v1/friends/requests", new { Username = usernameB })).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var item = await WaitForInboxItemAsync(userB, InboxItemType.FriendRequestReceived);
        item.ActorUserId.Should().Be(userA);

        await using var db = _factory.CreateDbContext();
        (await db.InboxItems.CountAsync(i => i.UserId == userB && i.Type == InboxItemType.FriendRequestReceived))
            .Should().Be(1);
    }

    [Fact]
    public async Task FriendRequestAccepted_TriggersInboxItem_ForOriginalSender()
    {
        var (tokenA, userA, _) = await RegisterAndLoginAsync("ib_fra_a");
        var (tokenB, userB, usernameB) = await RegisterAndLoginAsync("ib_fra_b");

        await BefriendAsync(tokenA, tokenB, usernameB);

        var item = await WaitForInboxItemAsync(userA, InboxItemType.FriendRequestAccepted);
        item.ActorUserId.Should().Be(userB);
    }

    [Fact]
    public async Task ChannelInviteSent_TriggersInboxItem_ForInvitee()
    {
        var (ownerToken, ownerId, _) = await RegisterAndLoginAsync("ib_ci_owner");
        var (_, targetId, targetUsername) = await RegisterAndLoginAsync("ib_ci_target");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var create = await _client.PostAsJsonAsync("/api/v1/channels", new
        {
            Name = "ib-ch-" + Guid.NewGuid().ToString("N")[..8],
            IsPrivate = true,
            IsAiAssistantEnabled = false,
            DefaultPermissions = (long)ChannelPermission.ViewChannel,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var channelId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("channelId").GetGuid();

        (await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/invites", new { Username = targetUsername }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var item = await WaitForInboxItemAsync(targetId, InboxItemType.ChannelInviteReceived);
        item.ActorUserId.Should().Be(ownerId);
    }

    [Fact]
    public async Task ConversationStarted_TriggersInboxItem_OnlyForNonFriends_AndOnlyOnce()
    {
        var (tokenA, _, _) = await RegisterAndLoginAsync("ib_dm_a");
        var (_, userB, _) = await RegisterAndLoginAsync("ib_dm_b");

        // Create a conversation, send the first DM, A and B are not friends.
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var convResp = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = userB });
        convResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversationId = (await convResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("conversationId").GetGuid();

        (await _client.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages", new
        {
            Content = "hello",
            IdempotencyKey = Guid.NewGuid().ToString(),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        await WaitForInboxItemAsync(userB, InboxItemType.ConversationStarted);

        // Sending a second DM in the same conversation should not produce a second ConversationStarted event.
        (await _client.PostAsJsonAsync($"/api/v1/conversations/{conversationId}/messages", new
        {
            Content = "again",
            IdempotencyKey = Guid.NewGuid().ToString(),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        // Give the worker time to (not) act, then verify the count is still 1.
        await Task.Delay(TimeSpan.FromSeconds(3));
        await using var db = _factory.CreateDbContext();
        (await db.InboxItems.CountAsync(i => i.UserId == userB && i.Type == InboxItemType.ConversationStarted))
            .Should().Be(1);
        (await db.OutboxMessages.CountAsync(o => o.EventType == "ConversationStarted"))
            .Should().Be(1, "second message in the same conversation must not emit ConversationStarted again");

        // Friends scenario: C and D become friends first, then DM; no ConversationStarted should be emitted.
        var (tokenC, _, _) = await RegisterAndLoginAsync("ib_dm_c");
        var (tokenD, userD, usernameD) = await RegisterAndLoginAsync("ib_dm_d");
        await BefriendAsync(tokenC, tokenD, usernameD);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenC);
        var convCD = await _client.PostAsJsonAsync("/api/v1/conversations", new { OtherUserId = userD });
        convCD.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversationCD = (await convCD.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("conversationId").GetGuid();

        (await _client.PostAsJsonAsync($"/api/v1/conversations/{conversationCD}/messages", new
        {
            Content = "friend dm",
            IdempotencyKey = Guid.NewGuid().ToString(),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        await Task.Delay(TimeSpan.FromSeconds(3));
        await using var db2 = _factory.CreateDbContext();
        (await db2.InboxItems.CountAsync(i => i.UserId == userD && i.Type == InboxItemType.ConversationStarted))
            .Should().Be(0, "friends never trigger ConversationStarted inbox items");
    }

    [Fact]
    public async Task Redelivery_OfSameEvent_IsIdempotent()
    {
        var (_, _, _) = await RegisterAndLoginAsync("ib_idem_a"); // ensure users schema is non-empty
        var receiverId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var payload = new FriendRequestSentPayload(requestId, senderId, receiverId, DateTime.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);

        var sbClient = _factory.Services.GetRequiredService<ServiceBusClient>();
        await using var sender = sbClient.CreateSender(SocialEventTopics.Friend);

        async Task PublishOnce()
        {
            var msg = new ServiceBusMessage(body)
            {
                Subject = SocialEventTypes.FriendRequestSent,
                MessageId = Guid.NewGuid().ToString(),
            };
            await sender.SendMessageAsync(msg);
        }

        await PublishOnce();
        await PublishOnce();

        // Poll until at least one row arrives, then confirm only one exists.
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = _factory.CreateDbContext();
            if (await db.InboxItems.AnyAsync(i => i.UserId == receiverId && i.ReferenceId == requestId))
                break;
            await Task.Delay(PollInterval);
        }

        // Give the second delivery time to be processed (and idempotency-rejected).
        await Task.Delay(TimeSpan.FromSeconds(3));

        await using var dbFinal = _factory.CreateDbContext();
        (await dbFinal.InboxItems.CountAsync(i => i.UserId == receiverId && i.ReferenceId == requestId))
            .Should().Be(1, "InboxWorker.ExistsAsync probe must deduplicate by (UserId, Type, ReferenceId)");
    }

    [Fact]
    public async Task GetInbox_NewestFirst_AndCursorRoundtrips()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("ib_pg_recv");

        // Seed 5 InboxItems directly (newest last in insert order so we can assert ordering).
        await using (var db = _factory.CreateDbContext())
        {
            for (int i = 0; i < 5; i++)
            {
                var item = InboxItem.Create(
                    Guid.NewGuid(), userId, InboxItemType.FriendRequestReceived,
                    Guid.NewGuid(), Guid.NewGuid());
                db.InboxItems.Add(item);
                await db.SaveChangesAsync();
                await Task.Delay(15);
            }
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var page1 = await _client.GetAsync("/api/v1/inbox?take=2");
        page1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await page1.Content.ReadFromJsonAsync<JsonElement>();
        var items1 = body1.GetProperty("items").EnumerateArray().ToList();
        items1.Should().HaveCount(2);
        var ts1 = items1.Select(i => i.GetProperty("createdAt").GetDateTime()).ToList();
        ts1.Should().BeInDescendingOrder();

        var cursor = body1.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrWhiteSpace();

        var page2 = await _client.GetAsync($"/api/v1/inbox?take=2&cursor={Uri.EscapeDataString(cursor!)}");
        page2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await page2.Content.ReadFromJsonAsync<JsonElement>();
        var items2 = body2.GetProperty("items").EnumerateArray().ToList();
        items2.Should().HaveCount(2);

        // No duplicates across pages.
        var ids1 = items1.Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        var ids2 = items2.Select(i => i.GetProperty("id").GetGuid()).ToHashSet();
        ids1.Intersect(ids2).Should().BeEmpty();

        // Items strictly newer-first across the page boundary.
        items2[0].GetProperty("createdAt").GetDateTime()
            .Should().BeOnOrBefore(items1.Last().GetProperty("createdAt").GetDateTime());
    }

    [Fact]
    public async Task Dismiss_SetsActedOnTrue_ButRowRemainsVisible()
    {
        var (token, userId, _) = await RegisterAndLoginAsync("ib_dis_recv");

        Guid itemId;
        await using (var db = _factory.CreateDbContext())
        {
            var item = InboxItem.Create(
                Guid.NewGuid(), userId, InboxItemType.FriendRequestReceived,
                Guid.NewGuid(), Guid.NewGuid());
            db.InboxItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var dismiss = await _client.PostAsync($"/api/v1/inbox/{itemId}/dismiss", content: null);
        dismiss.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = _factory.CreateDbContext();
        var updated = await db2.InboxItems.SingleAsync(i => i.Id == itemId);
        updated.IsActedOn.Should().BeTrue();

        var get = await _client.GetAsync("/api/v1/inbox");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(itemId, "dismiss must not remove the row from the inbox listing");
    }
}
