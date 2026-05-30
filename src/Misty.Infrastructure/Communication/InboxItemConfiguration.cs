using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class InboxItemConfiguration : IEntityTypeConfiguration<InboxItem>
{
    public void Configure(EntityTypeBuilder<InboxItem> builder)
    {
        builder.ToTable("InboxItem", SchemaNames.Comm);

        builder.HasKey(i => i.Id);

        builder.Property(i => i.UserId).IsRequired();
        builder.Property(i => i.ActorUserId).IsRequired();

        builder.Property(i => i.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(i => i.IsActedOn)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasIndex(i => new { i.UserId, i.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_InboxItem_User_CreatedAt");

        builder.HasIndex(i => new { i.UserId, i.IsActedOn })
            .HasDatabaseName("IX_InboxItem_User_IsActedOn");
    }
}
