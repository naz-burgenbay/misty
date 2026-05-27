using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class MessageReactionConfiguration : IEntityTypeConfiguration<MessageReaction>
{
    public void Configure(EntityTypeBuilder<MessageReaction> builder)
    {
        builder.ToTable("MessageReaction", SchemaNames.Msg);

        // Composite primary key enforces one reaction of a given emoji per user per message.
        builder.HasKey(r => new { r.MessageId, r.UserId, r.EmojiCode });

        // Binary collation is required so SQL Server distinguishes between distinct emoji codes.
        builder.Property(r => r.EmojiCode)
            .IsRequired()
            .HasMaxLength(64)
            .UseCollation("Latin1_General_100_BIN2");

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
