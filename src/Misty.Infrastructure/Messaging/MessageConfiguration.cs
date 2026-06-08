using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Message", SchemaNames.Msg, t =>
            t.HasCheckConstraint(
                "CK_Message_ChannelOrConversation",
                "([ChannelId] IS NOT NULL AND [ConversationId] IS NULL) OR ([ChannelId] IS NULL AND [ConversationId] IS NOT NULL)"));

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Content)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(m => m.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(m => new { m.AuthorId, m.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_Message_Author_IdempotencyKey");

        builder.Property(m => m.Version)
            .IsRowVersion();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(m => m.ParentMessageId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
