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
public sealed class DismissInboxItemTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public DismissInboxItemTests(ApiFactory factory)
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

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<Guid> SeedInboxItemAsync(Guid forUserId)
    {
        await using var db = _factory.CreateDbContext();
        var item = InboxItem.Create(
            Guid.NewGuid(), forUserId, InboxItemType.FriendRequestReceived,
            actorUserId: Guid.NewGuid(), referenceId: Guid.NewGuid());
        db.InboxItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    [Fact]
    public async Task Dismiss_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.PostAsync($"/api/v1/inbox/{Guid.NewGuid()}/dismiss", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dismiss_OwnItem_Returns204_AndMarksActedOn()
    {
        var (token, userId) = await RegisterAndLoginAsync("ib_dis_own");
        var itemId = await SeedInboxItemAsync(userId);

        SetToken(token);
        var resp = await _client.PostAsync($"/api/v1/inbox/{itemId}/dismiss", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var item = await db.InboxItems.SingleAsync(i => i.Id == itemId);
        item.IsActedOn.Should().BeTrue();
    }

    [Fact]
    public async Task Dismiss_NonExistentItem_Returns404()
    {
        var (token, _) = await RegisterAndLoginAsync("ib_dis_404");
        SetToken(token);

        var resp = await _client.PostAsync($"/api/v1/inbox/{Guid.NewGuid()}/dismiss", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dismiss_ItemOwnedByAnotherUser_Returns403_AndDoesNotMutate()
    {
        var (_, ownerId) = await RegisterAndLoginAsync("ib_dis_owner");
        var (attackerToken, _) = await RegisterAndLoginAsync("ib_dis_attacker");
        var itemId = await SeedInboxItemAsync(ownerId);

        SetToken(attackerToken);
        var resp = await _client.PostAsync($"/api/v1/inbox/{itemId}/dismiss", content: null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using var db = _factory.CreateDbContext();
        var item = await db.InboxItems.SingleAsync(i => i.Id == itemId);
        item.IsActedOn.Should().BeFalse("a forbidden dismiss must not mutate the row");
    }

    [Fact]
    public async Task Dismiss_AlreadyDismissedItem_IsIdempotent()
    {
        var (token, userId) = await RegisterAndLoginAsync("ib_dis_idem");
        var itemId = await SeedInboxItemAsync(userId);

        SetToken(token);
        (await _client.PostAsync($"/api/v1/inbox/{itemId}/dismiss", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _client.PostAsync($"/api/v1/inbox/{itemId}/dismiss", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await using var db = _factory.CreateDbContext();
        var item = await db.InboxItems.SingleAsync(i => i.Id == itemId);
        item.IsActedOn.Should().BeTrue();
    }
}
