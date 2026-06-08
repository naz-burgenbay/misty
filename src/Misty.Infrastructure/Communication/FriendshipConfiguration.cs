using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendship", SchemaNames.Comm, t =>
            t.HasCheckConstraint("CK_Friendship_UserAltUserB", "[UserAId] < [UserBId]"));

        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserAId).IsRequired();
        builder.Property(f => f.UserBId).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();

        builder.Property(f => f.Version)
            .IsRowVersion();

        builder.HasIndex(f => new { f.UserAId, f.UserBId })
            .IsUnique()
            .HasDatabaseName("UX_Friendship_UserA_UserB");
    }
}
