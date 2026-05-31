using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Infrastructure.Communication;
using Misty.Tests.Integration;
using NSubstitute;
using Respawn;

namespace Misty.Tests.Communication;

// Tests for plan-execution.md Step 5.6.2.a — event publishing is transactional.
// (1) An integration test proving a kick is reflected in CachedPermissionService within one outbox-relay cycle.
// (2) A unit test on the publisher proving its only side-effect is an outbox write; it does not touch Service Bus directly, so a Service Bus outage cannot abort an HTTP mutation.
[Collection("Integration")]
public sealed class TransactionalEventPublishTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private Respawner _respawner = default!;

    public TransactionalEventPublishTests(ApiFactory factory)
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

    [Fact]
    public async Task Kick_WritesOutboxRowImmediately_AndCacheReflectsKickWithinOneRelayCycle()
    {
        var (ownerToken, _) = await RegisterAndLoginAsync("txpub_owner1");
        var (memberToken, memberId) = await RegisterAndLoginAsync("txpub_member1");
        var channelId = await CreateChannelAsync(ownerToken, "txpub-ch1");
        await JoinChannelAsync(memberToken, channelId);

        // Give the owner ManageMembers via a role so the kick is authorised.
        // (Owner inherits all permissions; no role grant needed.)

        // Warm the permission cache: member must initially have ViewChannel via a granted role.
        var roleId = await CreateRoleAsync(ownerToken, channelId, ChannelPermission.ViewChannel);
        await AssignRoleAsync(ownerToken, channelId, memberId, roleId);

        // Wait for setup-time invalidation events to drain so a freshly-warmed cache stays valid.
        await Task.Delay(3000);

        using (var preScope = _factory.Services.CreateScope())
        {
            var pre = await preScope.ServiceProvider
                .GetRequiredService<IPermissionService>()
                .CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
            pre.Should().BeTrue("member should have ViewChannel via the assigned role before the kick");
        }

        // Kick the member.
        SetToken(ownerToken);
        var kickResp = await _client.DeleteAsync($"/api/v1/channels/{channelId}/members/{memberId}");
        kickResp.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        // Immediately after the HTTP response: a membership-events outbox row must exist.
        // Proves the publish is now part of the same DB-bound work, not an in-request Service Bus send.
        await using (var db = _factory.CreateDbContext())
        {
            var row = await db.OutboxMessages
                .Where(o => o.Topic == "membership-events" && o.EventType == "MembershipChanged")
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            row.Should().NotBeNull("the kick must enqueue an outbox row in the same transaction as the membership removal");
            row!.Payload.Should().Contain(memberId.ToString());
            row.Payload.Should().Contain(channelId.ToString());
        }

        // Within one or two outbox-relay cycles (~1 s each) plus a Service Bus + cache-invalidation hop,
        // CachedPermissionService should reflect the kick.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        var stillHas = true;
        while (DateTime.UtcNow < deadline && stillHas)
        {
            await Task.Delay(250);
            using var scope = _factory.Services.CreateScope();
            stillHas = await scope.ServiceProvider
                .GetRequiredService<IPermissionService>()
                .CheckPermissionAsync(memberId, channelId, ChannelPermission.ViewChannel);
        }

        stillHas.Should().BeFalse(
            "the cached permission must be invalidated after the outbox row is relayed and CacheInvalidationWorker processes the message");
    }

    [Fact]
    public async Task ServiceBusEventPublisher_Publish_OnlyEnqueuesAnOutboxRow()
    {
        // Unit test: the publisher must not touch Service Bus directly. Its only collaborator
        // is IOutboxWriter, so an outage of Service Bus cannot abort a permission mutation.
        var outbox = Substitute.For<IOutboxWriter>();
        var publisher = new ServiceBusEventPublisher(outbox);

        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await publisher.PublishMembershipChangedAsync(userId, channelId);
        await publisher.PublishRoleChangedAsync(userId, channelId);
        await publisher.PublishRoleChangedAsync(userId: null, channelId);
        await publisher.PublishModerationActionAppliedAsync(userId, channelId);

        outbox.Received(1).WriteAsync(
            "membership-events", "MembershipChanged", channelId,
            Arg.Is<CacheInvalidationPayload>(p => p.UserId == userId && p.ChannelId == channelId),
            Arg.Any<CancellationToken>());

        outbox.Received(1).WriteAsync(
            "role-events", "RoleChanged", channelId,
            Arg.Is<CacheInvalidationPayload>(p => p.UserId == userId && p.ChannelId == channelId),
            Arg.Any<CancellationToken>());

        outbox.Received(1).WriteAsync(
            "role-events", "RoleChanged", channelId,
            Arg.Is<CacheInvalidationPayload>(p => p.UserId == null && p.ChannelId == channelId),
            Arg.Any<CancellationToken>());

        outbox.Received(1).WriteAsync(
            "moderation-events", "ModerationActionApplied", channelId,
            Arg.Is<CacheInvalidationPayload>(p => p.UserId == userId && p.ChannelId == channelId),
            Arg.Any<CancellationToken>());
    }

    private void SetToken(string token)
        => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

    private async Task JoinChannelAsync(string memberToken, Guid channelId)
    {
        SetToken(memberToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/join", new { });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateRoleAsync(string ownerToken, Guid channelId, ChannelPermission permissions)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsJsonAsync($"/api/v1/channels/{channelId}/roles", new
        {
            Name = "TxRole",
            Permissions = (long)permissions,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roleId").GetGuid();
    }

    private async Task AssignRoleAsync(string ownerToken, Guid channelId, Guid memberId, Guid roleId)
    {
        SetToken(ownerToken);
        var resp = await _client.PostAsync(
            $"/api/v1/channels/{channelId}/members/{memberId}/roles/{roleId}", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
