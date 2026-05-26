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

        // Payload is an unbounded JSON string; no max length constraint.
        builder.Property(m => m.Payload)
            .IsRequired();

        // rowversion is automatically included in UPDATE/DELETE WHERE clauses by EF Core.
        builder.Property(m => m.Version)
            .IsRowVersion();

        // Filtered index to make relay queries fast; only unpublished rows are ever scanned.
        builder.HasIndex(m => m.CreatedAt)
            .HasFilter("[PublishedAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessage_Unpublished_CreatedAt");
    }
}
