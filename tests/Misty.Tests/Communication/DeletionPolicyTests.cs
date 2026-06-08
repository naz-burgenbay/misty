using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;
using Misty.Domain.Users;
using Misty.Tests.Integration;
using Respawn;

namespace Misty.Tests.Communication;

[Collection("Integration")]
public sealed class DeletionPolicyTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private Respawner _respawner = default!;

    public DeletionPolicyTests(ApiFactory factory) => _factory = factory;

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

    private async Task<Guid> SeedUserAsync(string suffix)
    {
        await using var db = _factory.CreateDbContext();
        var user = User.Create(Guid.NewGuid(), $"u_{suffix}", $"{suffix}@test.misty", $"User {suffix}");
        user.SetPasswordHash("hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedChannelAsync(Guid creatorId, string name)
    {
        await using var db = _factory.CreateDbContext();
        var channel = Channel.Create(Guid.NewGuid(), name, isPrivate: false, isAiAssistantEnabled: false, ChannelPermission.ViewChannel, creatorId);
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        return channel.Id;
    }

    [Fact]
    public async Task User_SoftDelete_SetsFlagsAndClearsPii()
    {
        var userId = await SeedUserAsync("del_user");

        await using (var db = _factory.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var deleted = await verify.Users.SingleAsync(u => u.Id == userId);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        deleted.DisplayName.Should().Be("[deleted]");
        deleted.AvatarUrl.Should().BeNull();
        deleted.PasswordHash.Should().BeEmpty();
    }

    [Fact]
    public async Task Channel_SoftDelete_SetsFlagsAndRowRemainsVisible()
    {
        var creatorId = await SeedUserAsync("del_channel_owner");
        var channelId = await SeedChannelAsync(creatorId, "del-channel");

        await using (var db = _factory.CreateDbContext())
        {
            var ch = await db.Channels.SingleAsync(c => c.Id == channelId);
            ch.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var deleted = await verify.Channels.SingleAsync(c => c.Id == channelId);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Conversation_SoftDelete_FiltersFromDefaultQuery()
    {
        var a = await SeedUserAsync("del_conv_a");
        var b = await SeedUserAsync("del_conv_b");
        var convId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Conversations.Add(Conversation.Create(convId, a, b));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var conv = await db.Conversations.SingleAsync(c => c.Id == convId);
            conv.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.Conversations.AnyAsync(c => c.Id == convId)).Should().BeFalse();
        var raw = await verify.Conversations.IgnoreQueryFilters().SingleAsync(c => c.Id == convId);
        raw.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
        raw.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ChannelRole_SoftDelete_FiltersFromDefaultQuery()
    {
        var creatorId = await SeedUserAsync("del_role_owner");
        var channelId = await SeedChannelAsync(creatorId, "del-role-ch");
        var roleId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.ChannelRoles.Add(ChannelRole.Create(roleId, channelId, "Custom", ChannelPermission.ViewChannel));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var role = await db.ChannelRoles.SingleAsync(r => r.Id == roleId);
            role.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.ChannelRoles.AnyAsync(r => r.Id == roleId)).Should().BeFalse();
        var raw = await verify.ChannelRoles.IgnoreQueryFilters().SingleAsync(r => r.Id == roleId);
        raw.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
        raw.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Membership_SoftDelete_FiltersFromDefaultQuery()
    {
        var ownerId = await SeedUserAsync("del_mem_owner");
        var memberId = await SeedUserAsync("del_mem_member");
        var channelId = await SeedChannelAsync(ownerId, "del-mem-ch");
        var membershipId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Memberships.Add(Membership.Create(membershipId, channelId, memberId));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var m = await db.Memberships.SingleAsync(x => x.Id == membershipId);
            m.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.Memberships.AnyAsync(x => x.Id == membershipId)).Should().BeFalse();
        var raw = await verify.Memberships.IgnoreQueryFilters().SingleAsync(x => x.Id == membershipId);
        raw.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
        raw.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ModerationAction_SoftDelete_RemainsVisibleWithFlagsSet()
    {
        var modId = await SeedUserAsync("del_mod_issuer");
        var targetId = await SeedUserAsync("del_mod_target");
        var channelId = await SeedChannelAsync(modId, "del-mod-ch");
        var actionId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.ModerationActions.Add(ModerationAction.Create(actionId, channelId, targetId, modId, ModerationActionType.Mute, "spam"));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var a = await db.ModerationActions.SingleAsync(x => x.Id == actionId);
            a.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var deleted = await verify.ModerationActions.SingleAsync(x => x.Id == actionId);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ChannelInvite_SoftDelete_RemainsVisibleWithFlagsSet()
    {
        var inviterId = await SeedUserAsync("del_inv_from");
        var inviteeId = await SeedUserAsync("del_inv_to");
        var channelId = await SeedChannelAsync(inviterId, "del-inv-ch");
        var inviteId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.ChannelInvites.Add(ChannelInvite.Create(inviteId, channelId, inviterId, inviteeId));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var inv = await db.ChannelInvites.SingleAsync(x => x.Id == inviteId);
            inv.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var deleted = await verify.ChannelInvites.SingleAsync(x => x.Id == inviteId);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task FriendRequest_SoftDelete_RemainsVisibleWithFlagsSet()
    {
        var senderId = await SeedUserAsync("del_fr_sender");
        var receiverId = await SeedUserAsync("del_fr_receiver");
        var requestId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.FriendRequests.Add(FriendRequest.Create(requestId, senderId, receiverId));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var req = await db.FriendRequests.SingleAsync(x => x.Id == requestId);
            req.SoftDelete();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var deleted = await verify.FriendRequests.SingleAsync(x => x.Id == requestId);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Message_Tombstone_ClearsContentAndRemainsVisible()
    {
        var authorId = await SeedUserAsync("del_msg_author");
        var channelId = await SeedChannelAsync(authorId, "del-msg-ch");
        var messageId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Messages.Add(Message.CreateForChannel(messageId, channelId, authorId, "secret content", Guid.NewGuid().ToString("N")));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var msg = await db.Messages.SingleAsync(m => m.Id == messageId);
            msg.Tombstone();
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        var tombstoned = await verify.Messages.SingleAsync(m => m.Id == messageId);
        tombstoned.IsDeleted.Should().BeTrue();
        tombstoned.DeletedAt.Should().NotBeNull();
        tombstoned.DeletedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        tombstoned.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task MessageReaction_HardDelete_RemovesRow()
    {
        var authorId = await SeedUserAsync("del_rx_author");
        var channelId = await SeedChannelAsync(authorId, "del-rx-ch");
        var messageId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Messages.Add(Message.CreateForChannel(messageId, channelId, authorId, "hi", Guid.NewGuid().ToString("N")));
            db.MessageReactions.Add(MessageReaction.Create(messageId, authorId, "👍"));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var rx = await db.MessageReactions.SingleAsync(r => r.MessageId == messageId);
            db.MessageReactions.Remove(rx);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.MessageReactions.AnyAsync(r => r.MessageId == messageId)).Should().BeFalse();
    }

    [Fact]
    public async Task Friendship_HardDelete_RemovesRow()
    {
        var a = await SeedUserAsync("del_fs_a");
        var b = await SeedUserAsync("del_fs_b");
        var fsId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Friendships.Add(Friendship.Create(fsId, a, b));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var fs = await db.Friendships.SingleAsync(f => f.Id == fsId);
            db.Friendships.Remove(fs);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.Friendships.AnyAsync(f => f.Id == fsId)).Should().BeFalse();
    }

    [Fact]
    public async Task UserBlock_HardDelete_RemovesRow()
    {
        var blockerId = await SeedUserAsync("del_ub_blocker");
        var blockedId = await SeedUserAsync("del_ub_blocked");

        await using (var db = _factory.CreateDbContext())
        {
            db.UserBlocks.Add(UserBlock.Create(blockerId, blockedId));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var ub = await db.UserBlocks.SingleAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId);
            db.UserBlocks.Remove(ub);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.UserBlocks.AnyAsync(x => x.BlockerId == blockerId && x.BlockedId == blockedId)).Should().BeFalse();
    }

    [Fact]
    public async Task MemberRole_HardDelete_RemovesRow()
    {
        var ownerId = await SeedUserAsync("del_mr_owner");
        var memberId = await SeedUserAsync("del_mr_member");
        var channelId = await SeedChannelAsync(ownerId, "del-mr-ch");
        var membershipId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var db = _factory.CreateDbContext())
        {
            db.Memberships.Add(Membership.Create(membershipId, channelId, memberId));
            db.ChannelRoles.Add(ChannelRole.Create(roleId, channelId, "Custom", ChannelPermission.ViewChannel));
            db.MemberRoles.Add(MemberRole.Create(membershipId, roleId));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateDbContext())
        {
            var mr = await db.MemberRoles.SingleAsync(x => x.MembershipId == membershipId && x.RoleId == roleId);
            db.MemberRoles.Remove(mr);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateDbContext();
        (await verify.MemberRoles.AnyAsync(x => x.MembershipId == membershipId && x.RoleId == roleId)).Should().BeFalse();
    }
}
