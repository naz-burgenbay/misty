using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlock", SchemaNames.Comm);

        builder.HasKey(b => new { b.BlockerId, b.BlockedId });

        builder.Property(b => b.CreatedAt).IsRequired();

        builder.Property(b => b.Version)
            .IsRowVersion();
    }
}
