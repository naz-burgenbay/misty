using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
using Misty.Domain.Messaging;
using Misty.Domain.Users;

namespace Misty.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelRole> ChannelRoles => Set<ChannelRole>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MemberRole> MemberRoles => Set<MemberRole>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<ChannelInvite> ChannelInvites => Set<ChannelInvite>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each configuration assigns its entity to the correct SQL schema (SchemaNames.Users, SchemaNames.Comm, or SchemaNames.Msg) via ToTable()
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
