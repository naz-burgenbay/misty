using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ChannelInviteConfiguration : IEntityTypeConfiguration<ChannelInvite>
{
    public void Configure(EntityTypeBuilder<ChannelInvite> builder)
    {
        builder.ToTable("ChannelInvite", SchemaNames.Comm);

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ChannelId).IsRequired();
        builder.Property(i => i.InvitedByUserId).IsRequired();
        builder.Property(i => i.InvitedUserId).IsRequired();

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(i => i.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.ChannelId, i.InvitedUserId })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("UX_ChannelInvite_Channel_Invited_Pending");

        builder.HasIndex(i => new { i.InvitedUserId, i.Status })
            .HasDatabaseName("IX_ChannelInvite_Invited_Status");
    }
}
