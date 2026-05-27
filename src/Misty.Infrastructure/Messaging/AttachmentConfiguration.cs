using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachment", SchemaNames.Msg, t =>
            t.HasCheckConstraint(
                "CK_Attachment_ExactlyOneOwner",
                "(" +
                    "([OwnerType] = 1 AND [MessageId] IS NOT NULL AND [AvatarUserId] IS NULL AND [ChannelIconChannelId] IS NULL) OR " +
                    "([OwnerType] = 2 AND [MessageId] IS NULL AND [AvatarUserId] IS NOT NULL AND [ChannelIconChannelId] IS NULL) OR " +
                    "([OwnerType] = 3 AND [MessageId] IS NULL AND [AvatarUserId] IS NULL AND [ChannelIconChannelId] IS NOT NULL)" +
                ")"));

        builder.HasKey(a => a.Id);

        builder.Property(a => a.OwnerType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.BlobContainer)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.BlobName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.CdnUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.MessageId)
            .HasDatabaseName("IX_Attachment_MessageId")
            .HasFilter("[MessageId] IS NOT NULL");

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
