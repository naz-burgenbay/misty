using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ChannelRoleConfiguration : IEntityTypeConfiguration<ChannelRole>
{
    public void Configure(EntityTypeBuilder<ChannelRole> builder)
    {
        builder.ToTable("ChannelRole", SchemaNames.Comm);

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Permissions)
            .HasConversion<long>();

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(r => r.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
