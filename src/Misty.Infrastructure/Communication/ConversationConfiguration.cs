using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversation", SchemaNames.Comm);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserAId).IsRequired();
        builder.Property(c => c.UserBId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        // Unique constraint enforces one conversation per user pair.
        // Combined with canonical ordering (UserAId < UserBId), (A,B) and (B,A) map to the same row.
        builder.HasIndex(c => new { c.UserAId, c.UserBId })
            .IsUnique()
            .HasDatabaseName("UX_Conversation_UserA_UserB");
    }
}
