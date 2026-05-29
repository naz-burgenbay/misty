using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ModerationActionConfiguration : IEntityTypeConfiguration<ModerationAction>
{
    public void Configure(EntityTypeBuilder<ModerationAction> builder)
    {
        builder.ToTable("ModerationAction", SchemaNames.Comm);

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(m => m.Reason)
            .IsRequired()
            .HasMaxLength(512);

        builder.HasOne<Channel>()
            .WithMany()
            .HasForeignKey(m => m.ChannelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => new { m.ChannelId, m.TargetUserId, m.Type })
            .HasDatabaseName("IX_ModerationAction_Channel_User_Type");
    }
}
