using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessage", SchemaNames.Msg);

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Topic)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.EventType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(m => m.Payload)
            .IsRequired();

        builder.Property(m => m.Version)
            .IsRowVersion();

        builder.HasIndex(m => m.CreatedAt)
            .HasFilter("[PublishedAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessage_Unpublished_CreatedAt");
    }
}
