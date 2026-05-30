using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("Channel", SchemaNames.Comm);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.Property(c => c.IconUrl)
            .HasMaxLength(512);

        builder.Property(c => c.InviteCode)
            .HasMaxLength(32);

        builder.Property(c => c.DefaultPermissions)
            .HasConversion<long>();

        builder.Property(c => c.Version)
            .IsRowVersion();

        builder.HasIndex(c => c.InviteCode)
            .IsUnique()
            .HasFilter("[InviteCode] IS NOT NULL")
            .HasDatabaseName("UX_Channel_InviteCode");
    }
}
