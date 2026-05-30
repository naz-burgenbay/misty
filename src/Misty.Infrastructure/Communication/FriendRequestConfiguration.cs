using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        builder.ToTable("FriendRequest", SchemaNames.Comm, t =>
            t.HasCheckConstraint("CK_FriendRequest_SenderNeReceiver", "[SenderId] <> [ReceiverId]"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.SenderId).IsRequired();
        builder.Property(r => r.ReceiverId).IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasIndex(r => new { r.SenderId, r.ReceiverId })
            .IsUnique()
            .HasDatabaseName("UX_FriendRequest_Sender_Receiver");

        builder.HasIndex(r => new { r.ReceiverId, r.Status })
            .HasDatabaseName("IX_FriendRequest_Receiver_Status");
    }
}
