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

        builder.HasKey(r => new { r.MessageId, r.UserId, r.EmojiCode });

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
