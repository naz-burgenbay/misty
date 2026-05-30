using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Membership", SchemaNames.Comm);

        builder.HasKey(m => m.Id);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        // Filtered unique index allows a new active membership row to coexist with prior soft-deleted (kicked) rows for the same (channel, user).
        builder.HasIndex(m => new { m.ChannelId, m.UserId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Membership_Channel_User");

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
