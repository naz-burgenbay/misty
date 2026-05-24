using Microsoft.EntityFrameworkCore;
using Misty.Domain.Communication;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each configuration assigns its entity to the correct SQL schema (SchemaNames.Users, SchemaNames.Comm, or SchemaNames.Msg) via ToTable()
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
