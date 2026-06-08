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

        builder.Property(m => m.Version)
            .IsRowVersion();

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ChannelId, m.UserId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_Membership_Channel_User");

        builder.HasQueryFilter(m => !m.IsDeleted);
    }
}
